namespace Ghozt.Theming;

/// <summary>Which pole a resolved theme sits at.</summary>
public enum ThemeMode
{
    Dark,
    Light,

    /// <summary>Follows the operating system, keeping the selected palette identity.</summary>
    System,
}

/// <summary>
/// One named Ghozt look, in light and dark.
/// </summary>
/// <remarks>
/// <para>
/// A palette declares only its identity - two brand colours, an accent, and the depth of its dark
/// surfaces - and every token the UI binds to is derived from those. That is what makes twelve
/// themes in two modes maintainable: surfaces, borders, hovers and washes all follow from the same
/// few decisions, and the text colours are pushed until they clear a contrast floor against the
/// surface they land on, so no combination can ship unreadable.
/// </para>
/// <para>
/// The token names are the CAPITAL-IZ suite's token names wherever a role already exists there.
/// Keeping them identical is what lets a design change be made once and read the same across the
/// suite. The <c>Ghozt.*</c>, <c>State.*</c>, <c>Authority.*</c>, <c>Evidence.*</c> and
/// <c>Focus.*</c> families are new, and are the product adaptation the design system admits.
/// </para>
/// </remarks>
public sealed record GhoztPalette
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    /// <summary>One sentence: what the palette is for, not what colour it is.</summary>
    public required string Description { get; init; }

    /// <summary>What the palette is best suited to, in operational terms.</summary>
    public required string SuitedTo { get; init; }

    public required string Primary { get; init; }

    public required string Secondary { get; init; }

    /// <summary>The ink and fill role: selection, focus, the primary action, the accent borders.</summary>
    public required string Accent { get; init; }

    /// <summary>The quiet companion: the third wash stop and the gradient tail.</summary>
    public required string AccentSoft { get; init; }

    public required string DarkBase { get; init; }

    public required string DarkPanel { get; init; }

    public required string DarkElevated { get; init; }

    /// <summary>The anchor the light ladder tints from. Light mode is declared, not merely inverted.</summary>
    public required string LightBase { get; init; }

    /// <summary>Overrides <see cref="Primary"/> as the whisper of brand through light surfaces.</summary>
    public string? LightTint { get; init; }

    /// <summary>The palette's own default surface opacity, 35-100.</summary>
    public int DefaultOpacity { get; init; } = 82;

    /// <summary>The finish this palette was designed to be seen in.</summary>
    public ThemeEffects DefaultEffects { get; init; } = ThemeEffects.Standard;

    /// <summary>Contrast floor for body text. WCAG AA for normal text.</summary>
    public const double TextContrast = 4.5;

    /// <summary>The four chips a preset card shows.</summary>
    public IReadOnlyList<string> Swatches => new[] { Primary, Secondary, Accent, AccentSoft };

    /// <summary>
    /// Returns a fill close to <paramref name="accent"/> that can carry legible text, and the text
    /// colour to use on it.
    /// </summary>
    /// <remarks>
    /// Mid-tone hues sit in a dead zone where neither black nor white clears the floor - a mid violet
    /// tops out around 4.4:1 either way. The fill keeps its hue and moves only lightness, in whichever
    /// direction is already closer, rather than leaving that trap for whoever adds the next palette.
    /// </remarks>
    private static (Rgba Fill, Rgba OnFill) MakeLegibleFill(Rgba accent)
    {
        var ink = new Rgba(0x0D, 0x10, 0x16);
        var paper = ColorMath.White;

        var towardsInk = ColorMath.ContrastRatio(ink, accent);
        var towardsPaper = ColorMath.ContrastRatio(paper, accent);
        var preferInk = towardsInk >= towardsPaper;
        var text = preferInk ? ink : paper;

        if (Math.Max(towardsInk, towardsPaper) >= TextContrast)
        {
            return (accent, text);
        }

        for (var step = 1; step <= 24; step++)
        {
            var candidate = preferInk
                ? ColorMath.Lighten(accent, step / 40.0)
                : ColorMath.Darken(accent, step / 40.0);

            if (ColorMath.ContrastRatio(text, candidate) >= TextContrast)
            {
                return (candidate, text);
            }
        }

        return (preferInk ? paper : ink, text);
    }

    /// <summary>
    /// Every colour token for this palette in the requested mode.
    /// </summary>
    /// <param name="dark">Which pole. <see cref="ThemeMode.System"/> is resolved before this point.</param>
    /// <param name="surfaceOpacityPercent">
    /// How much of what is behind shows through the panels, 35-100. It only affects the large
    /// translucent surfaces - text, borders and accents stay fully opaque, or the whole interface
    /// would wash out as the slider moves.
    /// </param>
    /// <param name="effects">
    /// The optional finish. It matters here, and not only in the widget layer, because a gradient
    /// wash lies between the text and its surface: the colour the eye compares against is the
    /// composite, so the wash has to be inside the contrast calculation rather than painted on
    /// afterwards.
    /// </param>
    public IReadOnlyDictionary<string, Rgba> BuildTokens(
        bool dark,
        int surfaceOpacityPercent = 82,
        ThemeEffects? effects = null)
    {
        var finish = (effects ?? ThemeEffects.Standard).Normalised();

        var primary = ColorMath.Parse(Primary);
        var secondary = ColorMath.Parse(Secondary);
        var accent = ColorMath.Parse(Accent);
        var accentSoft = ColorMath.Parse(AccentSoft);

        var alpha = Math.Clamp(surfaceOpacityPercent, 35, 100) / 100.0;

        // The backdrop each surface is composited over when measuring contrast: what the eye actually
        // sees once the desktop shows through.
        var baseSurface = dark ? ColorMath.Parse(DarkBase) : ColorMath.Parse(LightBase);
        var panel = dark ? ColorMath.Parse(DarkPanel) : ColorMath.White;
        var elevated = dark ? ColorMath.Parse(DarkElevated) : ColorMath.Darken(ColorMath.Parse(LightBase), 0.05);

        // A whisper of the brand through light surfaces, so a light mode still reads as this theme
        // rather than as generic white.
        if (!dark)
        {
            var tint = LightTint is null ? primary : ColorMath.Parse(LightTint);
            baseSurface = ColorMath.Mix(baseSurface, tint, 0.05);
            panel = ColorMath.Mix(panel, tint, 0.025);
            elevated = ColorMath.Mix(elevated, tint, 0.05);
        }

        var cardAlpha = Math.Max(0.30, alpha - 0.30);

        bool OnSide(Rgba seen) => dark
            ? ColorMath.Luminance(seen) <= ColorMath.InkCrossoverLuminance
            : ColorMath.Luminance(seen) >= ColorMath.InkCrossoverLuminance;

        // Pulls a surface toward its mode's pole until what the eye sees, once the desktop shows
        // through, is on the side of the crossover where the mode's ink wins by at least AA.
        //
        // Text is never asked to fix a surface set that straddles: no single ink serves both sides,
        // and no amount of pushing a foreground can repair it. Pulling the surface costs a shade of
        // the declared tone and buys a guarantee.
        Rgba HoldSide(Rgba surface, double surfaceAlpha, Rgba backdrop)
        {
            var pole = dark ? ColorMath.Black : ColorMath.White;
            for (var step = 0; step <= 24; step++)
            {
                var candidate = ColorMath.Mix(surface, pole, step / 24.0);
                if (OnSide(ColorMath.Flatten(candidate.WithAlpha(surfaceAlpha), backdrop)))
                {
                    return candidate;
                }
            }

            return pole;
        }

        baseSurface = HoldSide(baseSurface, 1.0, baseSurface);
        panel = HoldSide(panel, alpha, baseSurface);

        // The card is held at full opacity, not at its translucent alpha: Surface.CardSolid paints
        // the same tone with nothing behind it, and in both modes that opaque form is the harder of
        // the two - it is the one furthest from the mode's own base.
        elevated = HoldSide(elevated, 1.0, baseSurface);

        var baseSecondary = ColorMath.Mix(baseSurface, panel, 0.5);
        var panelSoft = ColorMath.Mix(panel, elevated, 0.5);

        var inset = dark ? ColorMath.Darken(baseSurface, 0.30) : ColorMath.Darken(elevated, 0.04);
        var input = HoldSide(dark ? ColorMath.Darken(panel, 0.35) : ColorMath.White, 1.0, baseSurface);
        var hover = dark ? ColorMath.Lighten(panel, 0.10) : ColorMath.Darken(elevated, 0.05);

        // The selected row is tinted with the accent rather than derived from panel or elevated, so
        // it sits on neither of their alpha lines and nothing else in the set bounds it. It carries
        // text in every list in the app, which makes leaving it out the most expensive omission
        // available.
        var selected = HoldSide(
            ColorMath.Mix(panel, accent, dark ? 0.22 : 0.20),
            Math.Min(1.0, alpha + 0.06),
            baseSurface);

        // ...and then bounded against the card, which is a separate problem from being on the right
        // side of the crossover.
        //
        // A very bright accent - Ghostwire's #63F5C8, Frost Glass's #6EF2D6 - tinted into the panel
        // produces a selected row LIGHTER than the card above it. That is wrong twice over. It reads
        // wrongly, because a selected row is a tint on a list, not an elevation above the card
        // holding it. And it wrecks the palette, because the lightest reading surface is what every
        // ink in the theme is pushed against: Ghostwire's selected row reached luminance 0.051
        // against a 0.018 card, which forced Fault to #FFEFF1 and Verified to #E4F7EE - a pale pink
        // and a pale mint that no operator could tell apart, on the two states it matters most to
        // distinguish. Body text bleached to #EFF2F6 for the same reason.
        //
        // So the tint is capped at the card's own luminance with a little headroom, keeping the row
        // visibly selected while leaving the card as the reading ceiling the inks are tuned to.
        Rgba BoundToCard(Rgba surface, double surfaceAlpha)
        {
            const double Headroom = 1.5;

            var pole = dark ? ColorMath.Black : ColorMath.White;
            var cardLuminance = ColorMath.Luminance(elevated);
            var ceiling = dark ? cardLuminance * Headroom : cardLuminance;
            var floor = dark ? 0.0 : 1.0 - ((1.0 - cardLuminance) * Headroom);

            for (var step = 0; step <= 24; step++)
            {
                var candidate = ColorMath.Mix(surface, pole, step / 24.0);
                var seen = ColorMath.Flatten(candidate.WithAlpha(surfaceAlpha), baseSurface);
                var luminance = ColorMath.Luminance(seen);

                if (dark ? luminance <= ceiling : luminance >= floor)
                {
                    return candidate;
                }
            }

            return surface;
        }

        selected = BoundToCard(selected, Math.Min(1.0, alpha + 0.06));

        // The same text token lands on more than one surface: a panel, a card inside it, the inputs
        // and the selected row. Those are not equally light, so clearing the floor against the panel
        // alone leaves the card a little short - which is exactly how a theme ships looking fine and
        // reading badly in the one place it matters.
        var baseReadingSurfaces = new List<Rgba>
        {
            ColorMath.Flatten(panel.WithAlpha(alpha), baseSurface),
            ColorMath.Flatten(elevated.WithAlpha(cardAlpha), baseSurface),
            elevated,
            ColorMath.Flatten(input, baseSurface),
            ColorMath.Flatten(selected.WithAlpha(Math.Min(1.0, alpha + 0.06)), baseSurface),

            // These two lie on alpha lines whose ends are already held, so monotonicity covers them
            // today. They are named anyway: the guarantee should not depend on a reader noticing that
            // a surface happens to be bounded by two others, and widening the set can only push a
            // text colour further from a background, never closer.
            ColorMath.Flatten(elevated.WithAlpha(Math.Min(1.0, alpha + 0.04)), baseSurface),
            ColorMath.Flatten(
                ColorMath.Mix(panel, baseSurface, 0.35).WithAlpha(Math.Min(1.0, alpha + 0.14)),
                baseSurface),
        };

        // The strongest form of a wash stop that leaves every surface it covers still on side.
        Rgba Admit(Rgba stop, double requestedAlpha)
        {
            for (var step = 0; step <= 20; step++)
            {
                var candidate = stop.WithAlpha(requestedAlpha * (1 - (step / 20.0)));
                if (baseReadingSurfaces.All(surface => OnSide(ColorMath.Flatten(candidate, surface))))
                {
                    return candidate;
                }
            }

            return stop.WithAlpha(0);
        }

        // Direction and intensity move geometry only and so play no part; opacity is the sole
        // gradient dial that changes what text is read against. The slider stays live at every value
        // - it simply cannot legislate a surface that no ink can serve.
        var (startAlpha, midAlpha, endAlpha) = finish.StopAlphas();
        var washStart = Admit(primary, startAlpha);
        var washMid = Admit(secondary, midAlpha);
        var washEnd = Admit(accentSoft, endAlpha);

        var readingSurfaces = new List<Rgba>(baseReadingSurfaces);
        if (finish.Gradient)
        {
            // Luminance is monotone along a straight line in RGB, so a foreground that clears the
            // floor against both ends of a stop pair clears it everywhere between them. Holding the
            // three stops over each surface therefore covers the whole wash, not three points on it.
            foreach (var surface in baseReadingSurfaces)
            {
                readingSurfaces.Add(ColorMath.Flatten(washStart, surface));
                readingSurfaces.Add(ColorMath.Flatten(washMid, surface));
                readingSurfaces.Add(ColorMath.Flatten(washEnd, surface));
            }
        }

        Rgba Legible(Rgba foreground, double minimumRatio = TextContrast) =>
            ColorMath.EnsureContrastAcross(foreground, readingSurfaces, minimumRatio);

        // Text is derived from the surface, then forced to clear the floor against it.
        var primaryText = Legible(dark ? new Rgba(0xF7, 0xF9, 0xFC) : new Rgba(0x10, 0x16, 0x22));
        var bodyText = Legible(dark ? new Rgba(0xE2, 0xE7, 0xEF) : new Rgba(0x1E, 0x27, 0x36));
        var secondaryText = Legible(dark ? new Rgba(0xB6, 0xC0, 0xCF) : new Rgba(0x45, 0x51, 0x63));
        var mutedText = Legible(dark ? new Rgba(0x93, 0x9F, 0xB2) : new Rgba(0x56, 0x62, 0x74));

        // The semantic family goes through the same guarantee as body text. The anchor is what the
        // eye must recognise; the published ink is the nearest legible form of it. Fill forms keep
        // the anchor, because a fill is a background rather than a foreground and carries its own
        // paired ink.
        //
        // Derived before the accent because the accent has to be kept clear of the authority gold,
        // and it is the accent that moves.
        Rgba SemanticInk(Rgba anchor) => Legible(dark ? ColorMath.Lighten(anchor, 0.06) : ColorMath.Darken(anchor, 0.30));

        // The semantic colours are NOT pushed apart from one another here, and that is a decision
        // rather than an omission.
        //
        // An earlier version did separate them, by mixing each toward the mode's pole until it
        // cleared a distance from the ones already placed. It made things worse: mixing toward pure
        // black or white destroys hue, and Phantom Violet's light mode drove Warning from amber to
        // #000000 - a "warning" colour that is simply black. The lever was wrong.
        //
        // What the design system actually requires is narrower than "all eight must differ". Three
        // confusions are forbidden: a fault must never look verified, a simulation must never look
        // observed, and an authority decision must never look like ordinary selection. Those pairs
        // are far apart in hue by construction; ThemeCatalogTests proves it for every palette, mode
        // and opacity.
        //
        // Verified (#42C98A) and Live (#6EF2D6) genuinely are adjacent greens, and they are meant to
        // be: observed and verified are consecutive states of the same progression. They are told
        // apart by their label and their glyph, because colour is never the sole carrier of status.
        var live = SemanticInk(GhoztSemantics.Live);
        var telemetry = SemanticInk(GhoztSemantics.Telemetry);
        var counterfactual = SemanticInk(GhoztSemantics.Counterfactual);
        var authority = SemanticInk(GhoztSemantics.Authority);
        var verified = SemanticInk(GhoztSemantics.Verified);
        var warning = SemanticInk(GhoztSemantics.Warning);
        var fault = SemanticInk(GhoztSemantics.Fault);
        var critical = SemanticInk(GhoztSemantics.Critical);

        // The accent used as ink has to be legible too, which is usually where a brand colour fails
        // on light surfaces: gold on white is around 2:1.
        var accentInk = Legible(dark ? ColorMath.Lighten(accent, 0.10) : ColorMath.Darken(accent, 0.28));
        var accentBright = Legible(dark ? ColorMath.Lighten(accent, 0.34) : ColorMath.Darken(accent, 0.45));
        var accentMuted = Legible(dark
            ? ColorMath.Mix(accent, secondaryText, 0.35)
            : ColorMath.Darken(accent, 0.18));

        var (accentFill, onAccent) = MakeLegibleFill(accent);

        // Keep the accent ink clear of the authority gold.
        //
        // Gold means one thing in this product: an approval, a complete evidence seal, a release
        // clearance or a CAPITAL-IZ provenance mark. If a palette's ordinary accent ink lands on the
        // same colour, an "Approve" label and any other emphasised label become the same colour, and
        // the surface then claims a decision nobody made.
        //
        // The accent moves and the gold does not, because gold is a fixed brand mark and the accent
        // is the palette's own variable. Operator Green needs this: its lime #B5F26E darkens to an
        // olive on light surfaces that lands within 59 of darkened gold.
        //
        // A palette that DECLARES gold as its accent is exempt, because there is nothing to move
        // apart - Redacted Mono is near-monochrome by design and gold is deliberately the only
        // colour in it. Those palettes rely on the label, the glyph and the authority rail to
        // separate an approval from ordinary emphasis, exactly as the rest of the system relies on
        // colour never being the sole carrier.
        var accentIsAuthority =
            ColorMath.PerceptualDistance(accent, GhoztSemantics.Authority) <= 24;

        if (!accentIsAuthority)
        {
            for (var step = 1; step <= 16 && ColorMath.PerceptualDistance(accentInk, authority) < 72; step++)
            {
                // Toward the accent's own most saturated form, which pulls it back to its own hue
                // rather than toward a pole - moving it away from gold without bleaching it.
                accentInk = Legible(ColorMath.Mix(accentInk, ColorMath.Saturate(accent), step / 16.0));
            }
        }

        var borderBase = dark ? ColorMath.Lighten(panel, 0.22) : ColorMath.Darken(panel, 0.16);

        var (authorityFill, onAuthority) = MakeLegibleFill(GhoztSemantics.Authority);
        var (criticalFill, onCritical) = MakeLegibleFill(GhoztSemantics.Critical);
        var (faultFill, onFault) = MakeLegibleFill(GhoztSemantics.Fault);

        return new Dictionary<string, Rgba>(StringComparer.Ordinal)
        {
            // ---- surfaces (translucent: the opacity slider acts here) ----
            ["Surface.Void"] = ColorMath.Darken(baseSurface, dark ? 0.35 : 0.0),
            ["Surface.Deep"] = baseSurface,
            ["Surface.Base"] = baseSurface,
            ["Surface.Panel"] = panel.WithAlpha(alpha),
            ["Surface.PanelDeep"] = baseSurface.WithAlpha(alpha),
            ["Surface.PanelSoft"] = panelSoft.WithAlpha(alpha),
            ["Surface.BaseSecondary"] = baseSecondary.WithAlpha(alpha),
            ["Surface.DeepOpaque"] = baseSurface.WithAlpha(Math.Min(1.0, alpha + 0.10)),
            ["Surface.DeepGlass"] = elevated.WithAlpha(Math.Max(0.35, alpha - 0.12)),
            ["Surface.Dialog"] = ColorMath.Mix(panel, baseSurface, 0.35).WithAlpha(Math.Min(1.0, alpha + 0.14)),
            ["Surface.DialogDeep"] = baseSurface,
            ["Surface.Overlay"] = ColorMath.Darken(baseSurface, dark ? 0.35 : 0.0).WithAlpha(0.98),
            ["Surface.OverlaySoft"] = baseSurface.WithAlpha(0.47),

            // ---- cards and rows ----
            ["Surface.Card"] = elevated.WithAlpha(cardAlpha),
            ["Surface.CardSolid"] = elevated,
            ["Surface.Elevated"] = elevated,
            ["Surface.CardFaint"] = elevated.WithAlpha(0.15),
            ["Surface.CardHover"] = hover.WithAlpha(0.32),
            ["Surface.CardStrong"] = elevated.WithAlpha(Math.Min(1.0, alpha + 0.04)),
            ["Surface.CardStrongHover"] = hover.WithAlpha(Math.Min(1.0, alpha + 0.10)),
            ["Surface.RowHover"] = hover.WithAlpha(0.22),
            ["Surface.Hover"] = hover,

            // ---- inputs and insets ----
            ["Surface.Input"] = input,
            ["Surface.InputPopup"] = input.WithAlpha(0.97),
            ["Surface.Inset"] = inset.WithAlpha(0.30),
            ["Surface.InsetSoft"] = inset.WithAlpha(0.18),
            ["Surface.InsetGlass"] = inset.WithAlpha(0.62),

            // ---- selection, tinted with the accent so it reads as this theme ----
            ["Surface.Selected"] = selected.WithAlpha(Math.Min(1.0, alpha + 0.06)),
            ["Surface.SelectedStrong"] = ColorMath.Mix(panel, accent, dark ? 0.34 : 0.30)
                .WithAlpha(Math.Min(1.0, alpha + 0.10)),
            ["Surface.GroupHeader"] = ColorMath.Mix(panel, accent, 0.16).WithAlpha(0.55),
            ["Surface.AccentWash"] = accent.WithAlpha(0.16),

            // ---- text ----
            ["Text.Primary"] = primaryText,
            ["Text.Body"] = bodyText,
            ["Text.Secondary"] = secondaryText,
            ["Text.Muted"] = mutedText,
            ["Text.Faint"] = Legible(ColorMath.Mix(mutedText, accent, 0.35)),
            ["Text.OnAccent"] = onAccent,
            ["Text.Disabled"] = Legible(mutedText, 3.0),

            // ---- accent family ----
            ["Accent.Color"] = accentInk,
            ["Accent.Text"] = accentInk,
            ["Accent.Fill"] = accentFill,
            ["Accent.Bright"] = accentBright,
            ["Accent.Muted"] = accentMuted,
            ["Accent.Dim"] = ColorMath.Mix(accent, secondary, 0.35),
            ["Accent.Border"] = accent.WithAlpha(0.42),
            ["Accent.BorderHover"] = accent.WithAlpha(0.62),
            ["Accent.BorderStrong"] = accent.WithAlpha(0.78),
            ["Accent.BorderSoft"] = accent.WithAlpha(0.34),
            ["Accent.Thumb"] = accent.WithAlpha(0.62),
            ["Accent.ThumbHover"] = accent.WithAlpha(0.85),
            ["Accent.ThumbDrag"] = accentBright,
            ["Action.Primary"] = accentFill,
            ["Action.Secondary"] = accentInk,

            // ---- borders: one base tone at many weights ----
            ["Border.Default"] = borderBase,
            ["Border.Strong"] = borderBase.WithAlpha(0.75),
            ["Border.Control"] = borderBase.WithAlpha(0.66),
            ["Border.Soft"] = borderBase.WithAlpha(0.55),
            ["Border.Subtle"] = borderBase.WithAlpha(0.34),
            ["Border.Panel"] = borderBase.WithAlpha(0.45),
            ["Border.Card"] = borderBase.WithAlpha(0.38),
            ["Border.Faint"] = borderBase.WithAlpha(0.34),
            ["Border.Inset"] = borderBase.WithAlpha(0.28),
            ["Border.Dashed"] = borderBase.WithAlpha(0.26),
            ["Border.Ring"] = borderBase.WithAlpha(0.24),
            ["Border.RingFaint"] = borderBase.WithAlpha(0.16),
            ["Border.Selected"] = accent.WithAlpha(0.78),

            // ---- Ghozt semantic layer: fixed meaning, legible form ----
            ["Ghozt.Live"] = live,
            ["Ghozt.LiveSoft"] = GhoztSemantics.Live.WithAlpha(0.16),
            ["Ghozt.LiveBorder"] = GhoztSemantics.Live.WithAlpha(0.55),
            ["Ghozt.Telemetry"] = telemetry,
            ["Ghozt.TelemetrySoft"] = GhoztSemantics.Telemetry.WithAlpha(0.16),
            ["Ghozt.Counterfactual"] = counterfactual,
            ["Ghozt.CounterfactualSoft"] = GhoztSemantics.Counterfactual.WithAlpha(0.16),
            ["Ghozt.CounterfactualBorder"] = GhoztSemantics.Counterfactual.WithAlpha(0.62),

            ["State.Verified"] = verified,
            ["State.VerifiedSoft"] = GhoztSemantics.Verified.WithAlpha(0.16),
            ["State.Warning"] = warning,
            ["State.WarningSoft"] = GhoztSemantics.Warning.WithAlpha(0.16),
            ["State.Fault"] = fault,
            ["State.FaultSoft"] = GhoztSemantics.Fault.WithAlpha(0.16),
            ["State.FaultFill"] = faultFill,
            ["State.OnFault"] = onFault,
            ["State.Critical"] = critical,
            ["State.CriticalSoft"] = GhoztSemantics.Critical.WithAlpha(0.18),
            ["State.CriticalFill"] = criticalFill,
            ["State.OnCritical"] = onCritical,

            // Gold is authority, not emphasis. It appears only on an approval, a complete evidence
            // seal, a release clearance or a CAPITAL-IZ provenance mark.
            ["Authority.Clearance"] = authority,
            ["Authority.Fill"] = authorityFill,
            ["Authority.OnFill"] = onAuthority,
            ["Authority.Border"] = GhoztSemantics.Authority.WithAlpha(0.70),
            ["Authority.Soft"] = GhoztSemantics.Authority.WithAlpha(0.16),
            ["Evidence.Seal"] = authority,

            // ---- danger, kept as the suite names it so shared controls resolve ----
            ["Danger.Color"] = critical,
            ["Danger.Fill"] = criticalFill,
            ["Danger.Text"] = critical,
            ["Danger.Accent"] = ColorMath.Mix(GhoztSemantics.Critical, accent, 0.25),

            // Deliberately not any of the selected-state colours: a focus ring that reuses the
            // selection accent tells a keyboard operator nothing, because they cannot then see
            // whether the control they have landed on is also the one that is on.
            ["Focus.Ring"] = accentBright,

            // ---- gradient stops, for the layered glass washes ----
            ["Gradient.Start"] = primary.WithAlpha(dark ? 0.32 : 0.20),
            ["Gradient.Mid"] = secondary.WithAlpha(dark ? 0.22 : 0.14),
            ["Gradient.End"] = accentSoft.WithAlpha(dark ? 0.26 : 0.16),
            ["Gradient.Sheen"] = new Rgba(255, 255, 255, (byte)(dark ? 16 : 40)),
            ["Gradient.Depth"] = ColorMath.Darken(baseSurface, 0.30).WithAlpha(dark ? 90.0 / 255.0 : 24.0 / 255.0),

            // ---- the full-app wash, admitted only as far as legibility allows ----
            ["Wash.Start"] = washStart,
            ["Wash.Mid"] = washMid,
            ["Wash.End"] = washEnd,

            // ---- glow colours, used by the shadow and bloom effects ----
            ["Glow.Active"] = GhoztSemantics.Live,
            ["Glow.Accent"] = accent,
            ["Glow.Danger"] = GhoztSemantics.Critical,
            ["Glow.Shadow"] = dark
                ? ColorMath.Black
                : ColorMath.Darken(ColorMath.Mix(primary, new Rgba(90, 100, 115), 0.7), 0.1),
        };
    }
}
