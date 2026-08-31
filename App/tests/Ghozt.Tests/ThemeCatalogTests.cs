using Ghozt.Theming;
using Xunit;

namespace Ghozt.Tests;

public sealed class ThemeCatalogTests
{
    [Fact]
    public void TheBlueprintDeclaresTwelvePalettes()
    {
        Assert.Equal(12, GhoztCatalog.All.Count);
    }

    [Fact]
    public void KeysAreUniqueAndLowerSnakeCase()
    {
        var keys = GhoztCatalog.All.Select(p => p.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(keys, k => Assert.Matches("^[a-z][a-z0-9_]*$", k));
    }

    [Fact]
    public void EveryPaletteDeclaresItsIntentAndItsUse()
    {
        // A description that says what a palette is FOR is what stops the catalogue becoming twelve
        // colour swatches nobody can choose between.
        Assert.All(GhoztCatalog.All, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Description));
            Assert.False(string.IsNullOrWhiteSpace(p.SuitedTo));
            Assert.EndsWith(".", p.Description, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void EveryDeclaredColourParses()
    {
        Assert.All(GhoztCatalog.All, p =>
        {
            foreach (var hex in new[]
                     {
                         p.Primary, p.Secondary, p.Accent, p.AccentSoft,
                         p.DarkBase, p.DarkPanel, p.DarkElevated, p.LightBase,
                     })
            {
                var parsed = ColorMath.Parse(hex);
                Assert.Equal(hex.ToUpperInvariant(), parsed.ToHex());
            }
        });
    }

    [Fact]
    public void TheDarkLadderAscends()
    {
        // base darker than panel darker than elevated. If the ladder inverts, depth reads backwards
        // and a card recedes behind the panel holding it.
        Assert.All(GhoztCatalog.All, p =>
        {
            var b = ColorMath.Luminance(ColorMath.Parse(p.DarkBase));
            var pl = ColorMath.Luminance(ColorMath.Parse(p.DarkPanel));
            var e = ColorMath.Luminance(ColorMath.Parse(p.DarkElevated));
            Assert.True(b <= pl, $"{p.Key}: darkBase is not below darkPanel");
            Assert.True(pl <= e, $"{p.Key}: darkPanel is not below darkElevated");
        });
    }

    [Fact]
    public void DefaultOpacityIsInRange()
    {
        Assert.All(GhoztCatalog.All, p => Assert.InRange(p.DefaultOpacity, 35, 100));
    }

    [Fact]
    public void AnUnknownKeyFallsBackRatherThanThrowing()
    {
        // A persisted preference can name a palette a later build renamed. A shell that refuses to
        // open because of a stale setting is a worse failure than one that opens in the default.
        Assert.Equal(GhoztCatalog.DefaultKey, GhoztCatalog.Find("no_such_palette").Key);
        Assert.Equal(GhoztCatalog.DefaultKey, GhoztCatalog.Find(null).Key);
        Assert.False(GhoztCatalog.Exists("no_such_palette"));
    }

    [Fact]
    public void EveryPaletteGeneratesTheSameTokenSetInBothModes()
    {
        var reference = new GhoztTokens(GhoztCatalog.DefaultKey, dark: true, 82).TokenNames.OrderBy(x => x).ToList();

        Assert.All(GhoztCatalog.All, p =>
        {
            foreach (var dark in new[] { true, false })
            {
                var names = new GhoztTokens(p.Key, dark, 82).TokenNames.OrderBy(x => x).ToList();
                Assert.Equal(reference, names);
            }
        });
    }

    [Fact]
    public void SemanticAnchorsAreIdenticalAcrossEveryPaletteAndMode()
    {
        // A palette may tint ordinary surfaces, but it must never make a fault look verified or a
        // simulation look observed. The published ink is pushed for legibility; the ANCHOR is fixed,
        // and the ink must stay recognisably the anchor's hue.
        foreach (var palette in GhoztCatalog.All)
        {
            foreach (var dark in new[] { true, false })
            {
                var tokens = new GhoztTokens(palette.Key, dark, 82);
                foreach (var (name, anchor) in GhoztSemantics.Anchors)
                {
                    var published = tokens[name];
                    var distance = ColorMath.PerceptualDistance(
                        ColorMath.Saturate(published),
                        ColorMath.Saturate(anchor));

                    Assert.True(
                        distance <= 150,
                        $"{palette.Key}/{(dark ? "dark" : "light")}: {name} drifted from its anchor " +
                        $"({published} vs {anchor}, distance {distance:F0})");
                }
            }
        }
    }

    [Fact]
    public void SoftSemanticFormsAreTheUntouchedAnchor()
    {
        // The soft forms are backgrounds, not inks, so they keep the exact anchor hue at low alpha.
        // If these were pushed too, a fault wash and a warning wash would converge.
        var tokens = new GhoztTokens("redacted_mono", dark: true, 82);

        Assert.Equal(GhoztSemantics.Fault.ToHex(), (tokens["State.FaultSoft"] with { A = 255 }).ToHex());
        Assert.Equal(GhoztSemantics.Verified.ToHex(), (tokens["State.VerifiedSoft"] with { A = 255 }).ToHex());
        Assert.Equal(GhoztSemantics.Live.ToHex(), (tokens["Ghozt.LiveSoft"] with { A = 255 }).ToHex());
    }

    /// <summary>
    /// The three confusions the design system forbids outright.
    /// </summary>
    /// <remarks>
    /// Deliberately these three and not "every semantic colour differs from every other". Verified
    /// and Live are adjacent greens on purpose - observed and verified are consecutive states of one
    /// progression - and forcing them apart is what drove an earlier build to render Phantom Violet's
    /// warning colour as pure black. Colour is never the sole carrier of status; the label and glyph
    /// separate the adjacent pairs. These three, however, must never converge, because each one is a
    /// case where the wrong reading would change a decision.
    /// </remarks>
    /// <summary>
    /// Palettes exempt from the authority-versus-accent separation, with the reason.
    /// </summary>
    /// <remarks>
    /// An exact list rather than a rule, so that adding a palette which collides forces a decision
    /// rather than absorbing one. An exemption that no longer corresponds to a real conflict is
    /// worse than no exemption: it reads as a decision that was made while actually covering
    /// nothing, so <see cref="ExemptionsStillDescribeARealConflict"/> holds this list honest.
    /// <para>
    /// Redacted Mono declares <c>#CDA661</c> - the authority gold itself - as its accent
    /// (GZT-UX-001 §03). That contradicts §02 and §17, which reserve gold for evidence seals,
    /// approvals and release clearance. The declaration is the blueprint's, so it stands here rather
    /// than being quietly rewritten; the palette is near-monochrome by design and gold is
    /// deliberately the only colour in it. In that palette an approval is separated from ordinary
    /// emphasis by its label, its glyph and the authority rail, exactly as the rest of the system
    /// relies on colour never being the sole carrier. This wants an owner's decision.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> AuthorityAccentExemptions = new(StringComparer.Ordinal)
    {
        ["redacted_mono"] = "Declares the authority gold #CDA661 as its own accent (GZT-UX-001 §03).",
    };

    public static TheoryData<string, string, string> ForbiddenConfusions()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var palette in GhoztCatalog.All)
        {
            // A fault must never look verified.
            data.Add(palette.Key, "State.Fault", "State.Verified");
            data.Add(palette.Key, "State.Critical", "State.Verified");

            // A simulation must never look observed.
            data.Add(palette.Key, "Ghozt.Counterfactual", "Ghozt.Live");

            // An authority decision must never look like ordinary emphasis.
            if (!AuthorityAccentExemptions.ContainsKey(palette.Key))
            {
                data.Add(palette.Key, "Authority.Clearance", "Accent.Text");
            }
        }

        return data;
    }

    [Fact]
    public void ExemptionsStillDescribeARealConflict()
    {
        // An exemption for a palette that no longer declares gold as its accent would be covering
        // nothing. If this fails, delete the entry - do not widen it.
        foreach (var (key, reason) in AuthorityAccentExemptions)
        {
            Assert.True(GhoztCatalog.Exists(key), $"Exemption names a palette that does not exist: {key}");
            Assert.False(string.IsNullOrWhiteSpace(reason), $"Exemption for {key} records no reason");

            var accent = ColorMath.Parse(GhoztCatalog.Find(key).Accent);
            var distance = ColorMath.PerceptualDistance(accent, GhoztSemantics.Authority);

            Assert.True(
                distance <= 24,
                $"{key} no longer declares a gold accent (distance {distance:F0}); remove its exemption.");
        }
    }

    [Fact]
    public void AnAuthorityDecisionNeverLooksLikeOrdinarySelection()
    {
        // The blueprint's rule names SELECTION, which is a surface treatment rather than an ink.
        // This holds for every palette including the exempt ones: even where gold is the accent ink,
        // the selected-row surface must not be gold, or a highlighted row would read as approved.
        foreach (var palette in GhoztCatalog.All)
        {
            foreach (var dark in new[] { true, false })
            {
                var t = new GhoztTokens(palette.Key, dark, 82);
                var selection = ColorMath.Flatten(t["Surface.SelectedStrong"], t["Surface.Deep"]);
                var seal = t["Authority.Fill"];

                var distance = ColorMath.PerceptualDistance(selection, seal);
                Assert.True(
                    distance >= 60,
                    $"{palette.Key}/{(dark ? "dark" : "light")}: the selected surface {selection} " +
                    $"is only {distance:F0} from the authority seal {seal}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ForbiddenConfusions))]
    public void ForbiddenPairsNeverConverge(string key, string left, string right)
    {
        foreach (var dark in new[] { true, false })
        {
            foreach (var opacity in new[] { 35, 82, 100 })
            {
                var t = new GhoztTokens(key, dark, opacity);
                var distance = ColorMath.PerceptualDistance(t[left], t[right]);

                Assert.True(
                    distance >= 60,
                    $"{key}/{(dark ? "dark" : "light")}/op{opacity}: {left} {t[left]} and " +
                    $"{right} {t[right]} are only {distance:F0} apart");
            }
        }
    }

    [Fact]
    public void EveryStateCarriesALabelAndAGlyphAsWellAsAColour()
    {
        // The guarantee that makes the adjacent-green decision above safe. If a state ever lost its
        // second and third channel, colour would become the sole carrier and the pairs the previous
        // test permits to be close would become genuinely ambiguous.
        foreach (var state in System.Enum.GetValues<GhoztState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(state.Label()), $"{state} has no label");
            Assert.False(string.IsNullOrWhiteSpace(state.Glyph()), $"{state} has no glyph");
            Assert.False(string.IsNullOrWhiteSpace(state.TokenName()), $"{state} has no token");
        }

        // Distinct labels, or two states would read identically to a screen reader.
        var labels = System.Enum.GetValues<GhoztState>().Select(s => s.Label()).ToList();
        Assert.Equal(labels.Count, labels.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void FocusRingIsNotTheSelectionColour()
    {
        // A focus ring that reuses the selection accent tells a keyboard operator nothing: they
        // cannot then see whether the control they have landed on is also the one that is on.
        foreach (var palette in GhoztCatalog.All)
        {
            var t = new GhoztTokens(palette.Key, dark: true, 82);
            Assert.NotEqual(t["Focus.Ring"].ToHex(), (t["Surface.SelectedStrong"] with { A = 255 }).ToHex());
        }
    }
}
