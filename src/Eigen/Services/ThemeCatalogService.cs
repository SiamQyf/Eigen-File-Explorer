using System.Windows;
using System.Windows.Media;
using Eigen.Models;

namespace Eigen.Services;

public static class ThemeCatalogService
{
    public const string DefaultDarkSkinId = "eigen-amber";
    public const string DefaultLightSkinId = "default";
    public const string DefaultSkinId = DefaultLightSkinId;

    public static IReadOnlyList<string> Categories { get; } =
    [
        "All",
        "Warm",
        "Cosmic",
        "Neon",
        "Nature",
        "Editorial",
        "Glass",
        "Translucent",
        "Cyber",
        "Pixel",
        "Brutal",
        "Heritage"
    ];

    public static IReadOnlyList<EigenSkin> Skins { get; } = BuildCatalog();

    public static EigenSkin GetById(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            var found = Skins.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
                return found;
        }
        return Skins.FirstOrDefault(s => string.Equals(s.Id, DefaultSkinId, StringComparison.OrdinalIgnoreCase)) ?? Skins[0];
    }

    public static ResourceDictionary CreateResourceDictionary(EigenSkin skin, string? fontFamilyOverride = null)
    {
        var dict = new ResourceDictionary();
        AddBrush(dict, "BgWindowBrush", skin.BgWindow);
        AddBrush(dict, "BgSidebarBrush", skin.BgSidebar);
        AddBrush(dict, "BgContentBrush", skin.BgContent);
        AddBrush(dict, "BgElevatedBrush", skin.BgElevated);
        AddBrush(dict, "BgInputBrush", skin.BgInput);
        AddBrush(dict, "BgHoverBrush", skin.BgHover);
        AddBrush(dict, "BgPressedBrush", skin.BgPressed);
        AddBrush(dict, "BgSelectionBrush", skin.BgSelection);
        AddBrush(dict, "BgSelectionHoverBrush", skin.BgSelectionHover);
        AddBrush(dict, "BorderSubtleBrush", skin.BorderSubtle);
        AddBrush(dict, "BorderStrongBrush", skin.BorderStrong);
        AddBrush(dict, "AccentBrush", skin.Accent);
        AddBrush(dict, "AccentHoverBrush", skin.AccentHover);
        AddBrush(dict, "AccentSoftBrush", skin.AccentSoft);
        AddBrush(dict, "TextPrimaryBrush", skin.TextPrimary);
        AddBrush(dict, "TextSecondaryBrush", skin.TextSecondary);
        AddBrush(dict, "TextMutedBrush", skin.TextMuted);
        AddBrush(dict, "TextOnAccentBrush", skin.TextOnAccent);
        AddBrush(dict, "DangerBrush", skin.Danger);
        AddBrush(dict, "DangerHoverBrush", skin.DangerHover);
        AddBrush(dict, "ProgressTrackBrush", skin.ProgressTrack);
        AddBrush(dict, "ScrollThumbBrush", skin.ScrollThumb);
        AddBrush(dict, "ScrollThumbHoverBrush", skin.ScrollThumbHover);

        // Aesthetic geometry, font, and gradient header brushes
        var effectiveFont = string.IsNullOrWhiteSpace(fontFamilyOverride) || string.Equals(fontFamilyOverride, "Auto", StringComparison.OrdinalIgnoreCase)
            ? skin.FontFamilyName
            : $"{fontFamilyOverride}, Segoe UI Variable Text, Segoe UI";
        dict["AppFont"] = new FontFamily(effectiveFont);
        dict["ControlCornerRadius"] = new CornerRadius(skin.ControlCornerRadius);
        dict["CardCornerRadius"] = new CornerRadius(skin.CardCornerRadius);
        dict["PillCornerRadius"] = new CornerRadius(skin.ControlCornerRadius <= 2 ? 2 : 12);
        dict["SurfaceBorderThickness"] = new Thickness(skin.SurfaceBorderThickness);

        if (!string.IsNullOrEmpty(skin.HeaderGradientEnd))
        {
            var headerGrad = new LinearGradientBrush(
                Parse(skin.BgSidebar),
                Parse(skin.HeaderGradientEnd),
                new Point(0, 0),
                new Point(1, 0));
            headerGrad.Freeze();
            dict["HeaderSurfaceBrush"] = headerGrad;
        }
        else
        {
            AddBrush(dict, "HeaderSurfaceBrush", skin.BgSidebar);
        }

        var heroGrad = new LinearGradientBrush(
            Parse(skin.AccentSoft),
            Parse(skin.BgContent),
            new Point(0, 0),
            new Point(1, 1));
        heroGrad.Freeze();
        dict["HomeHeroBrush"] = heroGrad;

        return dict;
    }

    public static double ContrastRatio(string hexA, string hexB)
    {
        var la = RelativeLuminance(Parse(hexA));
        var lb = RelativeLuminance(Parse(hexB));
        var hi = Math.Max(la, lb);
        var lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static void AddBrush(ResourceDictionary dict, string key, string hex)
    {
        var brush = new SolidColorBrush(Parse(hex));
        brush.Freeze();
        dict[key] = brush;
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    private static double RelativeLuminance(Color c)
    {
        static double Lin(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    private static string PickReadableOnAccent(Color accent)
    {
        var lumAccent = RelativeLuminance(accent);
        var lumWhite = 1.0;
        var lumDark = RelativeLuminance(Color.FromRgb(18, 18, 22));
        var contrastWhite = (lumWhite + 0.05) / (lumAccent + 0.05);
        var contrastDark = (lumAccent + 0.05) / (lumDark + 0.05);
        return contrastWhite >= 3.0 || contrastWhite >= contrastDark ? "#FFFFFF" : "#121216";
    }

    private static EigenSkin MakeSkin(
        string id,
        string name,
        string category,
        string mood,
        bool isDark,
        string bgWindow,
        string bgSidebar,
        string bgContent,
        string bgElevated,
        string accent,
        string textPrimary,
        string textSecondary,
        IconPackStyle iconPack = IconPackStyle.Native,
        string fontFamily = "Segoe UI Variable Text, Segoe UI",
        double controlCorner = 6.0,
        double cardCorner = 10.0,
        double borderThickness = 1.0,
        string? headerGradientEnd = null,
        bool isTranslucent = false)
    {
        var cWin = Parse(bgWindow);
        var cSide = Parse(bgSidebar);
        var cCont = Parse(bgContent);
        var cAcc = Parse(accent);
        var cTxt = Parse(textPrimary);
        var cSec = Parse(textSecondary);

        if (isDark)
        {
            return new EigenSkin
            {
                Id = id,
                Name = name,
                Category = category,
                Mood = mood,
                IsDark = true,
                BgWindow = bgWindow,
                BgSidebar = bgSidebar,
                BgContent = bgContent,
                BgElevated = bgElevated,
                BgInput = ToHex(Mix(cWin, cCont, 0.35)),
                BgHover = ToHex(Mix(cCont, cTxt, 0.08)),
                BgPressed = ToHex(Mix(cCont, cTxt, 0.14)),
                BgSelection = ToHex(Mix(cCont, cAcc, 0.24)),
                BgSelectionHover = ToHex(Mix(cCont, cAcc, 0.33)),
                BorderSubtle = ToHex(Mix(cCont, cTxt, 0.12)),
                BorderStrong = ToHex(Mix(cCont, cTxt, 0.24)),
                Accent = accent,
                AccentHover = ToHex(Mix(cAcc, Colors.White, 0.16)),
                AccentSoft = ToHex(Mix(cCont, cAcc, 0.18)),
                TextPrimary = textPrimary,
                TextSecondary = textSecondary,
                TextMuted = ToHex(Mix(cSec, cCont, 0.32)),
                TextOnAccent = PickReadableOnAccent(cAcc),
                Danger = "#E06C65",
                DangerHover = "#C42B1C",
                ProgressTrack = ToHex(Mix(cCont, cTxt, 0.12)),
                ScrollThumb = ToHex(Mix(cCont, cTxt, 0.24)),
                ScrollThumbHover = ToHex(Mix(cCont, cTxt, 0.38)),
                IconPack = iconPack,
                FontFamilyName = fontFamily,
                ControlCornerRadius = controlCorner,
                CardCornerRadius = cardCorner,
                SurfaceBorderThickness = borderThickness,
                HeaderGradientEnd = headerGradientEnd,
                IsTranslucent = isTranslucent
            };
        }

        return new EigenSkin
        {
            Id = id,
            Name = name,
            Category = category,
            Mood = mood,
            IsDark = false,
            BgWindow = bgWindow,
            BgSidebar = bgSidebar,
            BgContent = bgContent,
            BgElevated = bgElevated,
            BgInput = ToHex(Mix(cSide, Colors.White, 0.56)),
            BgHover = ToHex(Mix(cSide, cTxt, 0.06)),
            BgPressed = ToHex(Mix(cSide, cTxt, 0.12)),
            BgSelection = ToHex(Mix(cCont, cAcc, 0.17)),
            BgSelectionHover = ToHex(Mix(cCont, cAcc, 0.25)),
            BorderSubtle = ToHex(Mix(cSide, cTxt, 0.12)),
            BorderStrong = ToHex(Mix(cSide, cTxt, 0.23)),
            Accent = accent,
            AccentHover = ToHex(Mix(cAcc, Colors.Black, 0.14)),
            AccentSoft = ToHex(Mix(cCont, cAcc, 0.13)),
            TextPrimary = textPrimary,
            TextSecondary = textSecondary,
            TextMuted = ToHex(Mix(cSec, cCont, 0.26)),
            TextOnAccent = PickReadableOnAccent(cAcc),
            Danger = "#C43E37",
            DangerHover = "#C42B1C",
            ProgressTrack = ToHex(Mix(cSide, cTxt, 0.10)),
            ScrollThumb = ToHex(Mix(cSide, cTxt, 0.22)),
            ScrollThumbHover = ToHex(Mix(cSide, cTxt, 0.36)),
            IconPack = iconPack,
            FontFamilyName = fontFamily,
            ControlCornerRadius = controlCorner,
            CardCornerRadius = cardCorner,
            SurfaceBorderThickness = borderThickness,
            HeaderGradientEnd = headerGradientEnd,
            IsTranslucent = isTranslucent
        };
    }

    private static List<EigenSkin> BuildCatalog() =>
    [
        // === DEFAULT (1) ===
        MakeSkin("default", "Default", "Editorial", "Catppuccin Latte · Default", false, "#E6E9EF", "#DCE0E8", "#EFF1F5", "#FFFFFF", "#1E66F5", "#363A4F", "#5C5F77", IconPackStyle.NeonWire),

        // === TRANSLUCENT (4) — Windows 11 Acrylic frosted glass skins ===
        MakeSkin("acrylic-obsidian", "Acrylic Obsidian", "Translucent", "Dark Frosted Glass", true, "#720E1218", "#8A131822", "#9E161C28", "#EE1E2636", "#5AC8FA", "#F2F6FC", "#A8B5C9", IconPackStyle.GlassFrost, "Segoe UI Variable Text, Segoe UI", 8, 12, 1, null, isTranslucent: true),
        MakeSkin("acrylic-frost", "Acrylic Frost", "Translucent", "Light Frosted Glass", false, "#88EEF3FA", "#9CE6ECF6", "#B4F5F8FD", "#F4FFFFFF", "#0067C0", "#161B26", "#4B5568", IconPackStyle.GlassFrost, "Segoe UI Variable Text, Segoe UI", 8, 12, 1, null, isTranslucent: true),
        MakeSkin("smoked-amethyst", "Smoked Amethyst", "Translucent", "Velvet Violet Acrylic", true, "#74130E1D", "#8C1A1328", "#A01E172E", "#EE281F3D", "#C084FC", "#F5F0FF", "#B5A8CF", IconPackStyle.GlassFrost, "Segoe UI Variable Text, Segoe UI", 8, 12, 1, null, isTranslucent: true),
        MakeSkin("mint-aero", "Mint Aero", "Translucent", "Cool Mist Glass", false, "#88EAF5F2", "#9CE0EFEA", "#B4F3FAF8", "#F4FFFFFF", "#0D8A72", "#142420", "#4A615A", IconPackStyle.GlassFrost, "Segoe UI Variable Text, Segoe UI", 8, 12, 1, null, isTranslucent: true),

        // === WARM (10) ===
        MakeSkin("eigen-amber", "Eigen Amber", "Warm", "Espresso & Amber", true, "#161311", "#1D1916", "#231E1B", "#2C2622", "#D87A44", "#F4EDE7", "#B6A89D"),
        MakeSkin("eigen-birch", "Eigen Birch", "Warm", "Alabaster & Clay", false, "#F2ECE6", "#EAE2DA", "#FCF9F6", "#FFFFFF", "#B55321", "#221B17", "#665950"),
        MakeSkin("roasted-espresso", "Roasted Espresso", "Warm", "Dark Roast & Caramel", true, "#120E0C", "#191310", "#201814", "#29201B", "#E09F5A", "#F6EFE8", "#BEA996"),
        MakeSkin("sahara-sandstone", "Sahara Sandstone", "Warm", "Sunlit Sand & Sienna", false, "#F4EAD8", "#EDE0CA", "#FDF8EE", "#FFFDF8", "#B84A24", "#2B2016", "#6E5A47"),
        MakeSkin("volcanic-ember", "Volcanic Ember", "Warm", "Basalt & Molten Glow", true, "#121011", "#181416", "#1F191B", "#292124", "#F26430", "#F7ECE9", "#C2A6A0"),
        MakeSkin("autumn-maple", "Autumn Maple", "Warm", "Burgundy & Gold", true, "#180F12", "#201418", "#27191D", "#332126", "#E59A3C", "#F8EFF1", "#C6AAB0"),
        MakeSkin("honeycomb-linen", "Honeycomb Linen", "Warm", "Linen & Raw Honey", false, "#F5EFE0", "#EDE4CF", "#FEFBF3", "#FFFFFF", "#B87412", "#262013", "#6B5E42"),
        MakeSkin("copper-forge", "Copper Forge", "Warm", "Cast Iron & Copper", true, "#131416", "#191B1E", "#1F2226", "#282C31", "#D97F54", "#EFF1F4", "#A9B0BA"),
        MakeSkin("tuscan-terracotta", "Tuscan Terracotta", "Warm", "Limestone & Ochre", false, "#F3E6DF", "#EBDAD2", "#FCF6F3", "#FFFFFF", "#A8422B", "#261816", "#6D504B"),
        MakeSkin("candlelight-velvet", "Candlelight Velvet", "Warm", "Plum Cocoa & Champagne", true, "#151015", "#1C151C", "#241B23", "#2E232D", "#DFB16B", "#F6EEF5", "#BEAAB9"),

        // === COSMIC (10) ===
        MakeSkin("midnight-nebula", "Midnight Nebula", "Cosmic", "Deep Space & Violet", true, "#0D0E1A", "#131526", "#181B30", "#212540", "#9D7BFF", "#EEF0FF", "#A7ACC9"),
        MakeSkin("royal-obsidian", "Royal Obsidian", "Cosmic", "Onyx & 24K Gold", true, "#0C0C0E", "#121317", "#18191E", "#21232A", "#E2B95B", "#F5F3EE", "#ADA99E"),
        MakeSkin("aurora-borealis", "Aurora Borealis", "Cosmic", "Arctic Night & Emerald", true, "#091318", "#0E1C23", "#13242D", "#1A303C", "#38E0A8", "#E9FAF5", "#96BFB4"),
        MakeSkin("velvet-amethyst", "Velvet Amethyst", "Cosmic", "Dark Plum & Orchid", true, "#140E1C", "#1B1326", "#221830", "#2C1F3D", "#D670FF", "#F5ECFA", "#B9A0C9"),
        MakeSkin("starlight-sapphire", "Starlight Sapphire", "Cosmic", "Abyssal Navy & Cobalt", true, "#0A101D", "#0F172A", "#141F36", "#1C2947", "#5BA4FF", "#ECF3FF", "#9DB2D1"),
        MakeSkin("eclipse-crimson", "Eclipse Crimson", "Cosmic", "Graphite & Ruby", true, "#110D10", "#181216", "#20171D", "#2B1F27", "#F04E6A", "#F9EDF0", "#C0A0A9"),
        MakeSkin("moonstone-pearl", "Moonstone Pearl", "Cosmic", "Ice Silver & Sapphire", false, "#E9EEF6", "#DFE6F1", "#F7FAFE", "#FFFFFF", "#2B59C3", "#182032", "#515E78"),
        MakeSkin("astral-void", "Astral Void", "Cosmic", "Black Hole & Pulsar", true, "#07090F", "#0C101A", "#111624", "#181F33", "#38D9F5", "#EBF8FF", "#90B0C4"),
        MakeSkin("gilded-jade", "Gilded Jade", "Cosmic", "Imperial Jade & Gold", true, "#0B1411", "#101D19", "#152620", "#1D332B", "#D9B65B", "#EDF7F3", "#98B8AC"),
        MakeSkin("rose-quartz", "Rose Quartz", "Cosmic", "Blush Pearl & Berry", false, "#F5E9EE", "#EEDCE4", "#FDF7FA", "#FFFFFF", "#B03B6B", "#291620", "#6E4D5D"),

        // === NEON (10) ===
        MakeSkin("cyberpunk-2099", "Cyberpunk 2099", "Neon", "Night City Magenta", true, "#0B0A18", "#121026", "#181533", "#221E45", "#FF3D9A", "#F7ECFF", "#B4A3CF"),
        MakeSkin("synthwave-sunset", "Synthwave Sunset", "Neon", "Retro Arcade Coral", true, "#140B21", "#1C102E", "#24143B", "#2F1B4D", "#FF6E5A", "#FFF0F5", "#C4A3BD"),
        MakeSkin("matrix-terminal", "Matrix Terminal", "Neon", "Phosphor Black & Green", true, "#060B07", "#0A120C", "#0E1A11", "#142418", "#35E875", "#E6FCEB", "#88BA94"),
        MakeSkin("tokyo-rain", "Tokyo Rain", "Neon", "Wet Asphalt & Cyan", true, "#0D1117", "#121821", "#171F2B", "#1F2938", "#2CE6DC", "#EBF6FA", "#96B0BD"),
        MakeSkin("plasma-indigo", "Plasma Indigo", "Neon", "Ultraviolet Electric", true, "#0C0D20", "#12142E", "#171A3C", "#202450", "#5EE0FF", "#EDF4FF", "#9DA8CF"),
        MakeSkin("vaporwave-dream", "Vaporwave Dream", "Neon", "Lilac Haze & Mint", true, "#161124", "#1E1730", "#261E3D", "#31274E", "#4EF0C8", "#F4EEFF", "#B4A6D1"),
        MakeSkin("laser-acid", "Laser Acid", "Neon", "Carbon & Volt Lime", true, "#0E100E", "#141714", "#1B1F1B", "#242A24", "#B8F229", "#F2F7EB", "#A3B396"),
        MakeSkin("hologram-mist", "Hologram Mist", "Neon", "Frosted Prism & Sky", false, "#E7EFF7", "#DCE7F3", "#F6FAFE", "#FFFFFF", "#6A42E8", "#191B2E", "#525878"),
        MakeSkin("arcade-hyperblue", "Arcade Hyperblue", "Neon", "Cabinet Blue & Tangerine", true, "#091021", "#0E1830", "#132040", "#1A2B54", "#FF8A2B", "#EDF3FF", "#98ADCF"),
        MakeSkin("nightwire-crimson", "Nightwire Crimson", "Neon", "Stealth Carbon & Laser", true, "#0D0D10", "#131318", "#1A1A21", "#23232D", "#FF4242", "#F5F5F7", "#A4A4B3"),

        // === NATURE (10) ===
        MakeSkin("pacific-pine", "Pacific Pine", "Nature", "Evergreen & Moss", true, "#0D1511", "#121E18", "#172720", "#1F332A", "#68C98A", "#ECF7F0", "#9ABAA6"),
        MakeSkin("bioluminescent-reef", "Bioluminescent Reef", "Nature", "Ocean Trench & Aqua", true, "#071419", "#0B1D24", "#102730", "#163440", "#29E3D3", "#E8FAF9", "#8EBFBE"),
        MakeSkin("nordic-fjord", "Nordic Fjord", "Nature", "Granite & Glacial Ice", true, "#11161C", "#171E26", "#1D2630", "#25313E", "#74B9E8", "#EDF3F8", "#9EB1C2"),
        MakeSkin("matcha-morning", "Matcha Morning", "Nature", "Rice Paper & Matcha", false, "#ECEFE4", "#E2E7D7", "#F8FAF3", "#FFFFFF", "#4B7A3B", "#1E261A", "#56634E"),
        MakeSkin("misty-redwood", "Misty Redwood", "Nature", "Cedar Bark & Fog", true, "#141110", "#1C1715", "#241E1B", "#2F2723", "#D16D47", "#F5EEEB", "#B8A59E"),
        MakeSkin("lavender-meadow", "Lavender Meadow", "Nature", "Morning Mist & Iris", false, "#ECEAF5", "#E2DFEF", "#F9F8FD", "#FFFFFF", "#6A4CB8", "#201B30", "#595175"),
        MakeSkin("deep-monsoon", "Deep Monsoon", "Nature", "Storm Cloud & Teal", true, "#10161A", "#161E24", "#1C272E", "#24323B", "#4EC2C4", "#EBF4F5", "#98B0B3"),
        MakeSkin("sakura-blossom", "Sakura Blossom", "Nature", "Ivory & Cherry Petal", false, "#F6EBEE", "#EFE0E5", "#FEF8FA", "#FFFFFF", "#C94A76", "#291820", "#6E525E"),
        MakeSkin("moss-and-slate", "Moss & Slate", "Nature", "River Stone & Lichen", true, "#121514", "#181C1B", "#1F2422", "#282F2C", "#9AD14B", "#EFF5F1", "#A2B0A7"),
        MakeSkin("sunlit-citrus", "Sunlit Citrus", "Nature", "Warm Ivory & Mandarin", false, "#F4EFE2", "#ECE5D2", "#FDFBF5", "#FFFFFF", "#C76314", "#241E14", "#665A46"),

        // === EDITORIAL (10) ===
        MakeSkin("nord-polar", "Nord Polar", "Editorial", "Arctic Slate & Frost", true, "#242933", "#2B303C", "#2E3440", "#3B4252", "#88C0D0", "#ECEFF4", "#B4BED0"),
        MakeSkin("swiss-paper", "Swiss Paper", "Editorial", "Gallery White & Vermilion", false, "#ECECEC", "#E4E4E4", "#FAFAFA", "#FFFFFF", "#C92A2A", "#161616", "#595959"),
        MakeSkin("catppuccin-mocha", "Catppuccin Mocha", "Editorial", "Soothing Mauve Dark", true, "#181825", "#1E1E2E", "#242436", "#313244", "#CBA6F7", "#CDD6F4", "#A6ADC8"),
        MakeSkin("catppuccin-latte", "Catppuccin Latte", "Editorial", "Crisp Cream & Cobalt", false, "#E6E9EF", "#DCE0E8", "#EFF1F5", "#FFFFFF", "#1E66F5", "#363A4F", "#5C5F77"),
        MakeSkin("dracula-nocturne", "Dracula Nocturne", "Editorial", "Violet Slate & Pink", true, "#1E1F29", "#242633", "#282A36", "#343746", "#FF79C6", "#F8F8F2", "#B2B7D4"),
        MakeSkin("gruvbox-hearth", "Gruvbox Hearth", "Editorial", "Retro Charcoal & Mustard", true, "#1D2021", "#242424", "#282828", "#3C3836", "#FABD2F", "#EBDBB2", "#BDAE93"),
        MakeSkin("solarized-dusk", "Solarized Dusk", "Editorial", "Deep Oceanic & Cyan", true, "#00212B", "#002833", "#073642", "#0D4250", "#2AA198", "#EEE8D5", "#93A1A1"),
        MakeSkin("solarized-parchment", "Solarized Parchment", "Editorial", "Warm Cream & Teal", false, "#EEE8D5", "#E5DEC9", "#FDF6E3", "#FFFDF5", "#268BD2", "#2D3B40", "#586E75"),
        MakeSkin("monochrome-ink", "Monochrome Ink", "Editorial", "Pure Obsidian & Silver", true, "#0B0B0D", "#121215", "#18181C", "#222228", "#E6E6EA", "#F4F4F6", "#A4A4AE"),
        MakeSkin("alabaster-studio", "Alabaster Studio", "Editorial", "Minimal White & Indigo", false, "#EBECEF", "#E3E5EA", "#FFFFFF", "#FFFFFF", "#3B4CCA", "#171923", "#52586B"),

        // === GLASS (10) — GlassFrost icons, soft 10/14px rounded corners, gradient wash headers ===
        MakeSkin("liquid-sapphire", "Liquid Sapphire", "Glass", "Prism Frost & Sky", true, "#091224", "#0E1A33", "#122140", "#192C54", "#4CC9FF", "#EEF7FF", "#9BBBE0", IconPackStyle.GlassFrost, "Segoe UI Variable Text, Segoe UI", 9, 14, 1, "#172D54"),
        MakeSkin("frosted-orchid", "Frosted Orchid", "Glass", "Lavender Quartz Glass", false, "#EFEAF7", "#E6DFF3", "#FBF9FE", "#FFFFFF", "#8B42D6", "#221833", "#605178", IconPackStyle.GlassFrost, "Segoe UI Variable Text, Segoe UI", 9, 14, 1, "#F3E5F8"),
        MakeSkin("glacier-crystal", "Glacier Crystal", "Glass", "Arctic Ice & Mint", true, "#08171E", "#0C212B", "#102B38", "#16394A", "#5CE8D6", "#EAFDFC", "#93C2C2", IconPackStyle.GlassFrost, "Segoe UI Variable Text, Segoe UI", 9, 14, 1, "#163B47"),
        MakeSkin("pearl-iridescence", "Pearl Iridescence", "Glass", "Opal Mist & Teal", false, "#EAF2F4", "#E0ECEE", "#F8FCFD", "#FFFFFF", "#1F8A9C", "#172A2E", "#526B70", IconPackStyle.GlassFrost, "Segoe UI Variable Text, Segoe UI", 9, 14, 1, "#E5F0F7"),
        MakeSkin("smoked-obsidian-glass", "Smoked Obsidian", "Glass", "Smoke Glass & Silver", true, "#101216", "#16191F", "#1B1F27", "#242A34", "#9FB8E0", "#F0F4FA", "#A0AAB8", IconPackStyle.GlassFrost, "Segoe UI Variable Text, Segoe UI", 9, 14, 1, "#202A38"),
        MakeSkin("rosewater-mist", "Rosewater Mist", "Glass", "Blush Glass & Coral", false, "#F7EAEB", "#F0DFE1", "#FDF8F8", "#FFFFFF", "#C44761", "#2B171C", "#6E4F56", IconPackStyle.GlassFrost, "Segoe UI Variable Text, Segoe UI", 9, 14, 1, "#F6E4ED"),
        MakeSkin("boreal-emerald", "Boreal Emerald", "Glass", "Northern Jade Glass", true, "#081611", "#0D2019", "#112920", "#18382C", "#4CE09B", "#EBFBF3", "#94C2AA", IconPackStyle.GlassFrost, "Segoe UI Variable Text, Segoe UI", 9, 14, 1, "#15362A"),
        MakeSkin("celestial-opal", "Celestial Opal", "Glass", "Twilight & Apricot", true, "#111022", "#181630", "#1E1B3B", "#28244D", "#FF9E6E", "#F5F0FF", "#B0A8CC", IconPackStyle.GlassFrost, "Segoe UI Variable Text, Segoe UI", 9, 14, 1, "#2B1E45"),
        MakeSkin("sunbeam-prism", "Sunbeam Prism", "Glass", "Diamond White & Amber", false, "#F5EFE6", "#EDE4D8", "#FEFBF7", "#FFFFFF", "#C96F1A", "#261E14", "#6B5B4A", IconPackStyle.GlassFrost, "Segoe UI Variable Text, Segoe UI", 9, 14, 1, "#F7E6D2"),
        MakeSkin("abyssal-glass", "Abyssal Glass", "Glass", "Deep Sea Bioluminescence", true, "#05101A", "#091826", "#0D2033", "#132D47", "#2CE0FF", "#E8F9FF", "#8AB4CC", IconPackStyle.GlassFrost, "Segoe UI Variable Text, Segoe UI", 9, 14, 1, "#102C45"),

        // === CYBER (10) — NeonWire wireframe icons, Cascadia Mono font, crisp 3px chamfer corners ===
        MakeSkin("tron-grid", "Tron Grid", "Cyber", "Cobalt & Razor Cyan", true, "#050A14", "#081020", "#0C162B", "#11203D", "#2CEBFF", "#E8FCFF", "#84B8C9", IconPackStyle.NeonWire, "Cascadia Mono, Consolas", 3, 4, 1, "#0B243D"),
        MakeSkin("akira-redline", "Akira Redline", "Cyber", "Asphalt & Neon Scarlet", true, "#0A090C", "#110F14", "#17141C", "#211C28", "#FF3B4E", "#FAEEF0", "#B59CA1", IconPackStyle.NeonWire, "Cascadia Mono, Consolas", 3, 4, 1, "#2E1019"),
        MakeSkin("ghost-shell", "Ghost Shell", "Cyber", "Stealth & Phosphor Aqua", true, "#080E12", "#0C151B", "#111D24", "#182933", "#40F2B4", "#EAFDF6", "#8BB8A8", IconPackStyle.NeonWire, "Cascadia Mono, Consolas", 3, 4, 1, "#112E2B"),
        MakeSkin("synth-magenta", "Synth Magenta", "Cyber", "Indigo & Laser Pink", true, "#0C0718", "#130B26", "#1A1033", "#241647", "#FF42C6", "#FAEFFF", "#B59AC9", IconPackStyle.NeonWire, "Cascadia Mono, Consolas", 3, 4, 1, "#2D1140"),
        MakeSkin("amber-crt", "Amber CRT", "Cyber", "Monochrome Phosphor", true, "#0B0804", "#120D06", "#1A1309", "#241B0D", "#FFB238", "#FFF2D6", "#BD9E68", IconPackStyle.NeonWire, "Cascadia Mono, Consolas", 2, 3, 1, "#261908"),
        MakeSkin("green-phosphor", "Green Phosphor", "Cyber", "IBM P39 Terminal", true, "#040A05", "#071209", "#0B1A0D", "#102614", "#44F075", "#E8FCEB", "#7FB88A", IconPackStyle.NeonWire, "Cascadia Mono, Consolas", 2, 3, 1, "#0C2912"),
        MakeSkin("blueprint-cad", "Blueprint CAD", "Cyber", "Drafting Navy & White", true, "#081833", "#0B2145", "#0F2B59", "#153A75", "#70D6FF", "#F0F8FF", "#9EC2E6", IconPackStyle.NeonWire, "Cascadia Mono, Consolas", 2, 3, 1, "#14376E"),
        MakeSkin("ultraviolet-pulse", "Ultraviolet Pulse", "Cyber", "Void & Electric Lilac", true, "#090714", "#0F0C21", "#15112E", "#1E1840", "#B975FF", "#F4EEFF", "#A898C9", IconPackStyle.NeonWire, "Cascadia Mono, Consolas", 3, 4, 1, "#251447"),
        MakeSkin("white-vector-lab", "White Vector Lab", "Cyber", "CAD White & Cobalt Wire", false, "#E8EDF5", "#DDE5F0", "#F7FAFE", "#FFFFFF", "#1D4ED8", "#111A2E", "#4D5E7D", IconPackStyle.NeonWire, "Cascadia Mono, Consolas", 3, 4, 1, "#D6E2F5"),
        MakeSkin("solar-flare-grid", "Solar Flare Grid", "Cyber", "Obsidian & Plasma Gold", true, "#0C0908", "#140F0C", "#1C1410", "#261B15", "#FF9A2E", "#FFF3E6", "#BF9E82", IconPackStyle.NeonWire, "Cascadia Mono, Consolas", 3, 4, 1, "#301A0D"),

        // === PIXEL (10) — RetroPixel 8-bit icons, Cascadia Mono, razor-sharp 0-2px corners ===
        MakeSkin("gameboy-pocket", "GameBoy Pocket", "Pixel", "Olive Sage & Forest Ink", false, "#CAD4B4", "#BEC9A7", "#DCE4C9", "#E6EDD6", "#305428", "#182914", "#41573B", IconPackStyle.RetroPixel, "Cascadia Mono, Consolas", 0, 2, 2, null),
        MakeSkin("famicom-crimson", "Famicom Crimson", "Pixel", "Warm Cartridge & Red", false, "#F2E9DC", "#E8DEC9", "#FAF5EC", "#FFFFFF", "#C82829", "#241916", "#665249", IconPackStyle.RetroPixel, "Cascadia Mono, Consolas", 0, 2, 2, null),
        MakeSkin("commodore-64", "Commodore 64", "Pixel", "Royal 64 Blue & Sky", true, "#1B1B66", "#23237A", "#2B2B8F", "#3838A8", "#88A8FF", "#F2F4FF", "#B4C2F0", IconPackStyle.RetroPixel, "Cascadia Mono, Consolas", 0, 2, 2, null),
        MakeSkin("pico-8-cartridge", "PICO-8 Cartridge", "Pixel", "Midnight Ink & Cherry", true, "#121726", "#192033", "#1D2B53", "#273869", "#FF3B6B", "#FFF1E8", "#A8B2CF", IconPackStyle.RetroPixel, "Cascadia Mono, Consolas", 0, 2, 2, null),
        MakeSkin("snes-lavender", "SNES Lavender", "Pixel", "Retro Gray & Violet", false, "#E3E1EB", "#D7D4E3", "#F4F3F8", "#FFFFFF", "#6A4BC4", "#221E33", "#585270", IconPackStyle.RetroPixel, "Cascadia Mono, Consolas", 0, 2, 2, null),
        MakeSkin("outrun-cassette", "Outrun Cassette", "Pixel", "Arcade Night & Sunset", true, "#120A1E", "#1B0F2B", "#221336", "#2D1A47", "#FF8C3B", "#FFF2EC", "#BF9EB8", IconPackStyle.RetroPixel, "Cascadia Mono, Consolas", 0, 2, 2, "#38143B"),
        MakeSkin("dungeon-16-bit", "Dungeon 16-Bit", "Pixel", "Crypt Stone & Torch", true, "#141418", "#1C1C22", "#23232B", "#2E2E38", "#F0A830", "#F5EFE6", "#ACA79E", IconPackStyle.RetroPixel, "Cascadia Mono, Consolas", 0, 2, 2, null),
        MakeSkin("arcade-mint", "Arcade Mint", "Pixel", "Cabinet Black & Mint", true, "#0B1214", "#101A1D", "#152226", "#1D2F36", "#42F5B3", "#EBFAF5", "#94B8AD", IconPackStyle.RetroPixel, "Cascadia Mono, Consolas", 0, 2, 2, null),
        MakeSkin("amber-cartridge", "Amber Cartridge", "Pixel", "Charcoal & Coin Gold", true, "#12100E", "#1A1714", "#211E1A", "#2C2822", "#F7C331", "#FAF5E8", "#B8AE9A", IconPackStyle.RetroPixel, "Cascadia Mono, Consolas", 0, 2, 2, null),
        MakeSkin("bubblegum-8-bit", "Bubblegum 8-Bit", "Pixel", "Pastel Cloud & Bubble", false, "#F5EBF4", "#ECDDE9", "#FDF8FC", "#FFFFFF", "#D43B87", "#291623", "#6B4D62", IconPackStyle.RetroPixel, "Cascadia Mono, Consolas", 0, 2, 2, null),

        // === BRUTAL (10) — TactileBrutal bold inked icons, Bahnschrift font, 0px corners, 2px borders ===
        MakeSkin("neo-brutal-amber", "Neo Brutal Amber", "Brutal", "Cream Ink & Tangerine", false, "#F2EDE0", "#EAE2CE", "#FAF7EE", "#FFFFFF", "#E05212", "#141210", "#4A443B", IconPackStyle.TactileBrutal, "Bahnschrift, Segoe UI", 0, 0, 2, null),
        MakeSkin("bauhaus-primary", "Bauhaus Primary", "Brutal", "Off-White & Bauhaus Blue", false, "#EAE8E1", "#E0DDD4", "#F7F5F0", "#FFFFFF", "#1B4ED8", "#141414", "#4B4B4B", IconPackStyle.TactileBrutal, "Bahnschrift, Segoe UI", 0, 0, 2, null),
        MakeSkin("pop-art-magenta", "Pop Art Magenta", "Brutal", "Comic Ink & Hot Pink", true, "#0A0A0C", "#121216", "#18181D", "#222229", "#FF3D8E", "#FFFFFF", "#B8B8C2", IconPackStyle.TactileBrutal, "Bahnschrift, Segoe UI", 0, 0, 2, null),
        MakeSkin("acid-poster", "Acid Poster", "Brutal", "Carbon & Acid Volt", true, "#0B0C0A", "#121410", "#171A15", "#20241D", "#D0FF29", "#F7FAF2", "#A8B59B", IconPackStyle.TactileBrutal, "Bahnschrift, Segoe UI", 0, 0, 2, null),
        MakeSkin("swiss-red-grid", "Swiss Red Grid", "Brutal", "Pure White & Signal Red", false, "#EBEBEB", "#E0E0E0", "#FFFFFF", "#FFFFFF", "#D41C1C", "#121212", "#4F4F4F", IconPackStyle.TactileBrutal, "Bahnschrift, Segoe UI", 0, 0, 2, null),
        MakeSkin("cobalt-concrete", "Cobalt Concrete", "Brutal", "Dark Concrete & Cobalt", true, "#111317", "#181B21", "#1E222A", "#272C36", "#4F8CFF", "#F2F5FA", "#A2ABB8", IconPackStyle.TactileBrutal, "Bahnschrift, Segoe UI", 0, 0, 2, null),
        MakeSkin("manga-ink", "Manga Ink", "Brutal", "Manga Paper & Crimson", false, "#F0EFEA", "#E6E4DD", "#FBFBF8", "#FFFFFF", "#C92A37", "#141414", "#525252", IconPackStyle.TactileBrutal, "Bahnschrift, Segoe UI", 0, 0, 2, null),
        MakeSkin("volt-streetwear", "Volt Streetwear", "Brutal", "Stealth & Neon Lime", true, "#0D0E10", "#141518", "#1A1C20", "#24272E", "#E6F533", "#F7F8FA", "#A5AAB5", IconPackStyle.TactileBrutal, "Bahnschrift, Segoe UI", 0, 0, 2, null),
        MakeSkin("tangerine-zine", "Tangerine Zine", "Brutal", "Newsprint & Vivid Coral", false, "#F5EBE1", "#ECE0D3", "#FCF7F2", "#FFFFFF", "#D94814", "#1C1612", "#594A40", IconPackStyle.TactileBrutal, "Bahnschrift, Segoe UI", 0, 0, 2, null),
        MakeSkin("obsidian-monolith", "Obsidian Monolith", "Brutal", "Matte Black & Champagne", true, "#08080A", "#101014", "#15151A", "#1E1E24", "#F2C96D", "#F7F5F0", "#ABA89E", IconPackStyle.TactileBrutal, "Bahnschrift, Segoe UI", 0, 0, 2, null),

        // === HERITAGE (10) — DuotoneMinimal icons, Georgia editorial serif, warm 8px refinement ===
        MakeSkin("imperial-library", "Imperial Library", "Heritage", "Mahogany & Antique Brass", true, "#140E0C", "#1C1310", "#241915", "#2E211C", "#D4A656", "#F5EFE6", "#BDAF9F", IconPackStyle.DuotoneMinimal, "Georgia, Cambria, Segoe UI", 6, 8, 1, "#2B1B15"),
        MakeSkin("kyoto-washi", "Kyoto Washi", "Heritage", "Washi Paper & Sumi Indigo", false, "#EFECE4", "#E6E2D8", "#FAF8F2", "#FFFFFF", "#2C4C7E", "#1F2229", "#5C606B", IconPackStyle.DuotoneMinimal, "Georgia, Cambria, Segoe UI", 6, 8, 1, "#E5E7ED"),
        MakeSkin("versailles-velvet", "Versailles Velvet", "Heritage", "Midnight Jade & Gilt", true, "#0C1411", "#121E1A", "#182822", "#20362E", "#E0BC67", "#F4F6F0", "#A6B5AC", IconPackStyle.DuotoneMinimal, "Georgia, Cambria, Segoe UI", 6, 8, 1, "#1D3028"),
        MakeSkin("parchment-folio", "Parchment Folio", "Heritage", "Aged Vellum & Oxblood", false, "#F0E7D5", "#E6DBC5", "#FAF4E8", "#FFFDF7", "#8C2630", "#291B18", "#69544E", IconPackStyle.DuotoneMinimal, "Georgia, Cambria, Segoe UI", 6, 8, 1, "#EFE1D6"),
        MakeSkin("venetian-teal", "Venetian Teal", "Heritage", "Lagoon Charcoal & Gold", true, "#0B1416", "#111E21", "#16282C", "#1F363B", "#49B6B0", "#EDF6F7", "#9AB2B5", IconPackStyle.DuotoneMinimal, "Georgia, Cambria, Segoe UI", 6, 8, 1, "#173036"),
        MakeSkin("porcelain-blue", "Porcelain Blue", "Heritage", "Ming Porcelain & Cobalt", false, "#E9EEF4", "#DFE6EF", "#F9FBFE", "#FFFFFF", "#24529E", "#192233", "#526078", IconPackStyle.DuotoneMinimal, "Georgia, Cambria, Segoe UI", 6, 8, 1, "#E0E9F5"),
        MakeSkin("saffron-silk", "Saffron Silk", "Heritage", "Plum Noir & Saffron", true, "#170F16", "#20141E", "#291A26", "#362232", "#EB9C3B", "#F8F0F6", "#C2AAB9", IconPackStyle.DuotoneMinimal, "Georgia, Cambria, Segoe UI", 6, 8, 1, "#331D2C"),
        MakeSkin("botanical-herbarium", "Botanical Herbarium", "Heritage", "Warm Linen & Sage", false, "#ECEEE4", "#E2E5D8", "#F9FAF5", "#FFFFFF", "#4E784B", "#1F261D", "#586353", IconPackStyle.DuotoneMinimal, "Georgia, Cambria, Segoe UI", 6, 8, 1, "#E4ECDE"),
        MakeSkin("art-deco-noir", "Art Deco Noir", "Heritage", "Gatsby Black & Champagne", true, "#0D0E12", "#14151B", "#1A1C24", "#242733", "#E3C170", "#F6F3EB", "#B2AEA2", IconPackStyle.DuotoneMinimal, "Georgia, Cambria, Segoe UI", 4, 6, 1, "#212029"),
        MakeSkin("ivory-atelier", "Ivory Atelier", "Heritage", "Studio Ivory & Bronze", false, "#F3ECE4", "#EAE1D7", "#FCF9F5", "#FFFFFF", "#9E5132", "#261E19", "#695950", IconPackStyle.DuotoneMinimal, "Georgia, Cambria, Segoe UI", 6, 8, 1, "#EFE2D8")
    ];
}
