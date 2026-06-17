// Helpers/IconHelper.cs — v4.2 (build 45 / v1.2.0.2)
//
// KEY FIX: MakeAeroStateIcon() / SwapAeroStateIcon()
//   Creates an Image whose Style carries two DataTriggers:
//     • IsMouseOver = True  → swap Source to hoverRole PNG
//     • IsPressed   = True  → swap Source to pressedRole PNG  (wins over hover)
//   Triggers bind to the ANCESTOR Button via RelativeSource FindAncestor.
//   Result: hover/pressed PNG variants activate automatically — no event handlers needed.
//
// SwapIcon() still exists for buttons that only need a default-state icon
// (Settings, Theme, Fullscreen, PlayMode, Interval).

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoFrame.Helpers
{
    public enum IconRole
    {
        // Playback
        Play = 0, PlayHover = 1, PlayClicked = 2, PlayDisabled = 3,
        Stop = 4, StopHover = 5, StopClicked = 6,
        // Navigation
        Start            =  7, StartHover    =  8,
        StartClicked     =  9, StartUnavailable = 10,
        End              = 11, EndHover       = 12,
        EndClicked       = 13, EndUnavailable = 14,
        // UI actions
        Settings   = 15, Apply     = 16, Cancel   = 17,
        Done       = 18, Info      = 19, Question = 20,
        Magnify    = 21, Changelog = 22,
        // Drives/folders
        MultimediaFolder = 23, EmptyFolder    = 24, DownloadFolder = 25,
        EmptyDrive       = 26, ActiveDriveHDD = 27, DriveCDDVD     = 28,
        SystemDrive      = 29, NetDrive       = 30, DisconnectedNet= 31,
        SaveAs           = 32,
        // Status / update
        CancelRound = 33, Warning = 34, Defence = 35, Sync = 36, StopRed = 37,
    }

    public static class IconHelper
    {
        // (PNG filename in Resources/Icons/, MDL2 glyph fallback)
        private static readonly (string png, string mdl2)[] _map =
        {
            /* Play             */ ("play.png",                 "\uE768"),
            /* PlayHover        */ ("playHover.png",            "\uE768"),
            /* PlayClicked      */ ("playClicked.png",          "\uE768"),
            /* PlayDisabled     */ ("playDisabled.png",         "\uE768"),
            /* Stop             */ ("stop.png",                 "\uE71A"),
            /* StopHover        */ ("stopHover.png",            "\uE71A"),
            /* StopClicked      */ ("stopClicked.png",          "\uE71A"),
            /* Start            */ ("start.png",                "\uE892"),
            /* StartHover       */ ("startHover.png",           "\uE892"),
            /* StartClicked     */ ("startClicked.png",         "\uE892"),
            /* StartUnavailable */ ("startUnavailable.png",     "\uE892"),
            /* End              */ ("end.png",                  "\uE893"),
            /* EndHover         */ ("endHover.png",             "\uE893"),
            /* EndClicked       */ ("endClicked.png",           "\uE893"),
            /* EndUnavailable   */ ("endUnavailable.png",       "\uE893"),
            /* Settings         */ ("102.png",                  "\uE713"),
            /* Apply            */ ("apply.png",                "\uE73E"),
            /* Cancel           */ ("cancel.png",               "\uE711"),
            /* Done             */ ("done.png",                 "\uE8FB"),
            /* Info             */ ("info.png",                 "\uE946"),
            /* Question         */ ("question.png",             "\uE9CE"),
            /* Magnify          */ ("magnify.png",              "\uE71E"),
            /* Changelog        */ ("checkAndAccept.png",       "\uE8F4"),
            /* MultimediaFolder */ ("multimediaFolder.ico",     "\uE8B7"),
            /* EmptyFolder      */ ("emptyFolderAero.ico",      "\uE8B7"),
            /* DownloadFolder   */ ("downloadFolder.png",       "\uE896"),
            /* EmptyDrive       */ ("emptyDrive.png",           "\uEDA2"),
            /* ActiveDriveHDD   */ ("activeDriveSSDandHDD.png", "\uEDA2"),
            /* DriveCDDVD       */ ("driveCDandDVD.png",        "\uE958"),
            /* SystemDrive      */ ("systemDrive.png",          "\uE977"),
            /* NetDrive         */ ("netDrive.png",             "\uE968"),
            /* DisconnectedNet  */ ("disconnectedNetDrive.png", "\uE9BA"),
            /* SaveAs           */ ("SaveAs.png",               "\uE792"),
            /* CancelRound      */ ("cancelRound.png",          "\uE894"),
            /* Warning          */ ("warning.png",              "\uE7BA"),
            /* Defence          */ ("defence.png",              "\uE8A4"),
            /* Sync             */ ("sync.png",                 "\uE895"),
            /* StopRed          */ ("stopRed.png",              "\uE71A"),
        };

        private static readonly BitmapImage?[] _cache = new BitmapImage?[_map.Length];
        private static readonly bool[]         _tried = new bool[_map.Length];

        // ── Bitmap loading ────────────────────────────────────────────────────

        public static BitmapImage? GetBitmap(IconRole role)
        {
            int i = (int)role;
            if (i >= _map.Length) return null;
            if (_tried[i]) return _cache[i];
            _tried[i] = true;
            try
            {
                var uri = new Uri(
                    $"pack://application:,,,/Resources/Icons/{_map[i].png}");
                var b = new BitmapImage(uri);
                b.Freeze();
                _cache[i] = b;
            }
            catch { _cache[i] = null; }
            return _cache[i];
        }

        public static string GetMdl2(IconRole role)
        {
            int i = (int)role;
            return i < _map.Length ? _map[i].mdl2 : "\uE711";
        }

        // ── Simple icon (no state) ─────────────────────────────────────────────

        /// <summary>
        /// Single PNG image, or MDL2 glyph if PNG not found.
        /// Use for buttons that don't need hover/pressed variants (Settings, Theme…).
        /// </summary>
        public static FrameworkElement MakeIcon(IconRole role, double size = 20)
        {
            var bmp = GetBitmap(role);
            if (bmp != null)
                return MakeRawImage(bmp, size);

            return new TextBlock
            {
                Text       = GetMdl2(role),
                FontFamily = new FontFamily("Segoe MDL2 Assets,Segoe UI Symbol"),
                FontSize   = size * 0.85,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
        }

        /// <summary>Swap first child of Button.Content StackPanel — simple state.</summary>
        public static void SwapIcon(Button btn, IconRole role, double size = 20)
        {
            if (btn?.Content is not StackPanel sp || sp.Children.Count == 0) return;
            var el = MakeIcon(role, size);
            el.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.RemoveAt(0);
            sp.Children.Insert(0, el);
        }

        // ── State-aware icon (hover + pressed PNG variants) ───────────────────
        //
        // Creates an Image with a Style containing two DataTriggers:
        //   Trigger 1 — Button.IsMouseOver = True  → Source = hoverBitmap
        //   Trigger 2 — Button.IsPressed   = True  → Source = pressedBitmap
        //                                             (listed last, wins over hover)
        // Both triggers bind via RelativeSource FindAncestor typeof(Button).
        // No event handlers, no code-behind polling — pure WPF binding.

        /// <summary>
        /// Create an Image that auto-swaps to hover/pressed PNG when
        /// the ancestor Button changes state.
        /// Falls back to MakeIcon() if the normal PNG is missing.
        /// </summary>
        public static FrameworkElement MakeAeroStateIcon(
            IconRole normalRole, IconRole hoverRole, IconRole pressedRole,
            double size = 20)
        {
            var bmpN = GetBitmap(normalRole);
            if (bmpN == null) return MakeIcon(normalRole, size); // MDL2 fallback

            var bmpH = GetBitmap(hoverRole)   ?? bmpN;
            var bmpP = GetBitmap(pressedRole) ?? bmpN;

            var img = MakeRawImage(bmpN, size);

            // Build Style with two DataTriggers
            var style = new Style(typeof(Image));

            // Trigger 1: IsMouseOver → hover PNG
            var trigHover = new DataTrigger
            {
                Binding = new Binding("IsMouseOver")
                {
                    RelativeSource = new RelativeSource(
                        RelativeSourceMode.FindAncestor, typeof(Button), 1)
                },
                Value = true
            };
            trigHover.Setters.Add(new Setter(Image.SourceProperty, bmpH));

            // Trigger 2: IsPressed → pressed PNG  (added last → wins when both true)
            var trigPress = new DataTrigger
            {
                Binding = new Binding("IsPressed")
                {
                    RelativeSource = new RelativeSource(
                        RelativeSourceMode.FindAncestor, typeof(Button), 1)
                },
                Value = true
            };
            trigPress.Setters.Add(new Setter(Image.SourceProperty, bmpP));

            style.Triggers.Add(trigHover);
            style.Triggers.Add(trigPress);

            img.Style = style;
            return img;
        }

        /// <summary>
        /// Swap first child of Button.Content StackPanel with a state-aware Aero icon.
        /// </summary>
        public static void SwapAeroStateIcon(
            Button btn,
            IconRole normalRole, IconRole hoverRole, IconRole pressedRole,
            double size = 20)
        {
            if (btn?.Content is not StackPanel sp || sp.Children.Count == 0) return;
            var el = MakeAeroStateIcon(normalRole, hoverRole, pressedRole, size);
            el.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.RemoveAt(0);
            sp.Children.Insert(0, el);
        }

        // ── Restore MDL2 glyph ────────────────────────────────────────────────

        /// <summary>Restore Segoe MDL2 glyph into first child of Button.Content StackPanel.</summary>
        public static void RestoreMdl2(Button btn, string glyph, double size = 20)
        {
            if (btn?.Content is not StackPanel sp) return;
            var tb = new TextBlock
            {
                Text       = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize   = size,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            if (sp.Children.Count > 0 && sp.Children[0] is Image)
            { sp.Children.RemoveAt(0); sp.Children.Insert(0, tb); }
            else if (sp.Children.Count > 0 && sp.Children[0] is TextBlock existing)
                existing.Text = glyph;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static Image MakeRawImage(BitmapImage bmp, double size)
        {
            var img = new Image
            {
                Source               = bmp,
                Width                = size,
                Height               = size,
                SnapsToDevicePixels  = true,
                UseLayoutRounding    = true,
                HorizontalAlignment  = HorizontalAlignment.Center,
                VerticalAlignment    = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }
    }
}
