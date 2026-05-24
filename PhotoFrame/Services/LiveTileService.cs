// Services/LiveTileService.cs — v4.1 (build 44 / v1.2.0.1)
//
// Live Tile update via WinRT reflection — no hard WinRT assembly dependency.
// Supports all 4 Start tile sizes: Small 71×71, Medium 150×150,
//                                  Wide 310×150, Large 310×310.
//
// Image delivery:
//   • photo path is passed as file:/// URI embedded in adaptive tile XML.
//   • Windows resolves the URI locally — works for any absolute local path.
//   • The image IS the background of each tile binding (no scaling artefacts).
//
// Tile XML spec (version="4", Windows 10+):
//   Small  — photo background only (no text, no branding — keeps it clean).
//   Medium — photo background only.
//   Wide   — photo background + app nameAndLogo branding (bottom-left),
//            hint-overlay="25" ensures logo is readable.
//   Large  — photo background + name branding + centred subtitle text,
//            hint-overlay="35" for readability on bright photos.
//
// Cycle queue:
//   EnqueuePhoto() — called on every slideshow advance (stores last 5 paths).
//   CycleTile()    — called by _tileTimer; rotates queue → different photo
//                    on tile even if slideshow moves faster than tile interval.
//   UpdateTile()   — immediate push (first photo / tile timer not running).
//
// includeLarge parameter:
//   false → omit <binding template="TileLarge"> from payload.
//   Users who use the Medium/Wide pin only should disable Large in Settings
//   to avoid a blank Large tile placeholder.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace PhotoFrame.Services
{
    public static class LiveTileService
    {
        // ─── AppUserModelId ───────────────────────────────────────────────────

        private const string AppId = "ArtemITuser.PhotoFrame";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        public static void SetAppUserModelId()
        {
            try { SetCurrentProcessExplicitAppUserModelID(AppId); }
            catch { }
        }

        // ─── Cycle queue (thread-safe via lock) ───────────────────────────────

        private static readonly object        _lock       = new();
        private static readonly List<string>  _queue      = new(5);
        private static          int           _queueIndex = 0;
        private const           int           QueueCap    = 5;

        /// <summary>
        /// Add path to rotation queue. Called on every slideshow photo change.
        /// Keeps last QueueCap unique paths; does not push a tile update itself.
        /// </summary>
        public static void EnqueuePhoto(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return;
            lock (_lock)
            {
                // Remove duplicate if present, then append
                int idx = _queue.FindIndex(
                    p => string.Equals(p, imagePath, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) _queue.RemoveAt(idx);
                _queue.Add(imagePath);
                if (_queue.Count > QueueCap) _queue.RemoveAt(0);
            }
        }

        /// <summary>
        /// Rotate the tile to the next queued photo.
        /// Call this from the independent tile timer.
        /// Returns true when a tile notification was sent.
        /// </summary>
        public static bool CycleTile(bool includeLarge = true)
        {
            string? path;
            lock (_lock)
            {
                if (_queue.Count == 0) return false;
                _queueIndex = (_queueIndex + 1) % _queue.Count;
                path = _queue[_queueIndex];
            }
            if (!File.Exists(path)) return false;
            try { PushTile(path, includeLarge); return true; }
            catch { return false; }
        }

        /// <summary>
        /// Push photo to tile immediately (used for first photo / on-demand).
        /// Also enqueues the path for future cycle rotation.
        /// </summary>
        public static void UpdateTile(string imagePath, bool includeLarge = true)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return;
            EnqueuePhoto(imagePath);
            try { PushTile(imagePath, includeLarge); }
            catch { }
        }

        /// <summary>Reset Start tile to default (app logo).</summary>
        public static void ClearTile()
        {
            lock (_lock) { _queue.Clear(); _queueIndex = 0; }
            try { ClearInternal(); }
            catch { }
        }

        // ─── Tile XML builder ─────────────────────────────────────────────────

        private static void PushTile(string imagePath, bool includeLarge)
        {
            // Convert Windows path to file:/// URI (spaces OK — WinRT handles them)
            string fileUri = "file:///" + imagePath.Replace('\\', '/');

            // Large binding is conditional
            string largeBinding = includeLarge ? $@"
    <binding template=""TileLarge"" branding=""name"" hint-overlay=""35"">
      <image src=""{fileUri}"" placement=""background"" hint-crop=""none""/>
      <text hint-style=""subtitle"" hint-align=""center"" hint-wrap=""true"">PhotoFrame</text>
    </binding>" : string.Empty;

            // Adaptive tile XML — version 4 (Windows 10 1607+)
            string xml =
$@"<tile>
  <visual version=""4"" displayName=""PhotoFrame"">
    <binding template=""TileSmall"">
      <image src=""{fileUri}"" placement=""background"" hint-crop=""none""/>
    </binding>
    <binding template=""TileMedium"">
      <image src=""{fileUri}"" placement=""background"" hint-crop=""none""/>
    </binding>
    <binding template=""TileWide"" branding=""nameAndLogo"" hint-overlay=""25"">
      <image src=""{fileUri}"" placement=""background"" hint-crop=""none""/>
    </binding>{largeBinding}
  </visual>
</tile>";

            var mgr = GetUpdater();
            if (mgr == null) return;

            var xmlDoc = CreateXmlDoc(xml);
            if (xmlDoc == null) return;

            var notifType = Type.GetType(
                "Windows.UI.Notifications.TileNotification, Windows, ContentType=WindowsRuntime");
            if (notifType == null) return;

            var notif = Activator.CreateInstance(notifType, xmlDoc);
            if (notif == null) return;

            mgr.GetType().GetMethod("Update")?.Invoke(mgr, new[] { notif });
        }

        private static void ClearInternal()
        {
            var mgr = GetUpdater();
            mgr?.GetType().GetMethod("Clear")?.Invoke(mgr, null);
        }

        // ─── WinRT reflection helpers ─────────────────────────────────────────

        private static object? GetUpdater()
        {
            var mgrType = Type.GetType(
                "Windows.UI.Notifications.TileUpdateManager, Windows, ContentType=WindowsRuntime");
            var method = mgrType?.GetMethod("CreateTileUpdaterForApplication",
                new[] { typeof(string) });
            return method?.Invoke(null, new object[] { AppId });
        }

        private static object? CreateXmlDoc(string xml)
        {
            var docType = Type.GetType(
                "Windows.Data.Xml.Dom.XmlDocument, Windows, ContentType=WindowsRuntime");
            if (docType == null) return null;
            var doc = Activator.CreateInstance(docType);
            if (doc == null) return null;
            docType.GetMethod("LoadXml")?.Invoke(doc, new object[] { xml });
            return doc;
        }
    }
}
