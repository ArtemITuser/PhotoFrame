// Helpers/IconHelper.cs — v3.7 (build 40)
// Complete Aero7 icon mapping with state-based icons (hover/clicked/disabled).
// Modern mode: uses Segoe MDL2 Assets glyphs only.
// Aero7 mode: PNG icons with hover/pressed/disabled states via triggers in XAML.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoFrame.Helpers
{
    public enum IconRole
    {
        // Playback
        Play = 0, PlayHover = 1, PlayClicked = 2, PlayDisabled = 3,
        Pause = 4,
        Stop = 5, StopHover = 6, StopClicked = 7,
        // Navigation
        Start = 8, StartHover = 9, StartClicked = 10, StartUnavailable = 11,
        End   = 12, EndHover   = 13, EndClicked   = 14, EndUnavailable   = 15,
        // UI actions
        Settings   = 16, Apply     = 17, Cancel   = 18,
        Done       = 19, Info      = 20, Question = 21,
        Magnify    = 22, Changelog = 23,
        // Drives/folders
        MultimediaFolder = 24, EmptyFolder   = 25, DownloadFolder = 26,
        EmptyDrive       = 27, ActiveDriveHDD= 28, DriveCDDVD     = 29,
        SystemDrive      = 30, NetDrive       = 31, DisconnectedNet= 32,
        SaveAs           = 33,
        // Status
        CancelRound = 34, Warning = 35, Defence = 36, Sync = 37, StopRed = 38,
    }

    public static class IconHelper
    {
        // (filename in Resources/Icons, Segoe MDL2 glyph, unicode fallback)
        private static readonly (string png, string mdl2)[] _map =
        {
            /* Play            */ ("play.png",               "\uE768"),
            /* PlayHover       */ ("playHover.png",          "\uE768"),
            /* PlayClicked     */ ("playClicked.png",        "\uE768"),
            /* PlayDisabled    */ ("playDisabled.png",       "\uE768"),
            /* Pause           */ ("play.png",               "\uE769"),
            /* Stop            */ ("stop.png",               "\uE71A"),
            /* StopHover       */ ("stopHover.png",          "\uE71A"),
            /* StopClicked     */ ("stopClicked.png",        "\uE71A"),
            /* Start           */ ("start.png",              "\uE892"),
            /* StartHover      */ ("startHover.png",         "\uE892"),
            /* StartClicked    */ ("startClicked.png",       "\uE892"),
            /* StartUnavailable*/ ("startUnavailable.png",   "\uE892"),
            /* End             */ ("end.png",                "\uE893"),
            /* EndHover        */ ("endHover.png",           "\uE893"),
            /* EndClicked      */ ("endClicked.png",         "\uE893"),
            /* EndUnavailable  */ ("endUnavailable.png",     "\uE893"),
            /* Settings        */ ("102.png",                "\uE713"),
            /* Apply           */ ("apply.png",              "\uE73E"),
            /* Cancel          */ ("cancel.png",             "\uE711"),
            /* Done            */ ("done.png",               "\uE8FB"),
            /* Info            */ ("info.png",               "\uE946"),
            /* Question        */ ("question.png",           "\uE9CE"),
            /* Magnify         */ ("magnify.png",            "\uE71E"),
            /* Changelog       */ ("checkAndAccept.png",     "\uE8F4"),
            /* MultimediaFolder*/ ("multimediaFolder.ico",   "\uE8B7"),
            /* EmptyFolder     */ ("emptyFolderAero.ico",    "\uE8B7"),
            /* DownloadFolder  */ ("downloadFolder.png",     "\uE896"),
            /* EmptyDrive      */ ("emptyDrive.png",         "\uEDA2"),
            /* ActiveDriveHDD  */ ("activeDriveSSDandHDD.png","\uEDA2"),
            /* DriveCDDVD      */ ("driveCDandDVD.png",      "\uE958"),
            /* SystemDrive     */ ("systemDrive.png",        "\uE977"),
            /* NetDrive        */ ("netDrive.png",           "\uE968"),
            /* DisconnectedNet */ ("disconnectedNetDrive.png","\uE9BA"),
            /* SaveAs          */ ("SaveAs.png",             "\uE792"),
            /* CancelRound     */ ("cancelRound.png",        "\uE894"),
            /* Warning         */ ("warning.png",            "\uE7BA"),
            /* Defence         */ ("defence.png",            "\uE8A4"),
            /* Sync            */ ("sync.png",               "\uE895"),
            /* StopRed         */ ("stopRed.png",            "\uE71A"),
        };

        private static readonly BitmapImage?[] _cache = new BitmapImage?[_map.Length];
        private static readonly bool[]         _tried = new bool[_map.Length];

        public static BitmapImage? GetBitmap(IconRole role)
        {
            int i = (int)role;
            if (i >= _map.Length) return null;
            if (_tried[i]) return _cache[i];
            _tried[i] = true;
            try
            {
                var uri = new Uri($"pack://application:,,,/Resources/Icons/{_map[i].png}");
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

        /// <summary>
        /// Creates icon element: PNG (Aero7) → Segoe MDL2 fallback.
        /// </summary>
        public static FrameworkElement MakeIcon(IconRole role, double size = 20)
        {
            var bmp = GetBitmap(role);
            if (bmp != null)
            {
                var img = new Image { Source = bmp, Width = size, Height = size,
                    SnapsToDevicePixels = true, UseLayoutRounding = true };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                return img;
            }
            return new TextBlock
            {
                Text       = GetMdl2(role),
                FontFamily = new FontFamily("Segoe MDL2 Assets,Segoe UI Symbol"),
                FontSize   = size * 0.85,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
        }

        /// <summary>Swap first child of Button.Content StackPanel to new icon.</summary>
        public static void SwapIcon(Button btn, IconRole role, double size = 20)
        {
            if (btn?.Content is not StackPanel sp || sp.Children.Count == 0) return;
            var el = MakeIcon(role, size);
            el.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.RemoveAt(0);
            sp.Children.Insert(0, el);
        }

        /// <summary>Restore Segoe MDL2 glyph to first child of Button.Content StackPanel.</summary>
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
            if (sp.Children.Count > 0 && sp.Children[0] is Image) { sp.Children.RemoveAt(0); sp.Children.Insert(0, tb); }
            else if (sp.Children.Count > 0 && sp.Children[0] is TextBlock existing) existing.Text = glyph;
        }
    }
}
