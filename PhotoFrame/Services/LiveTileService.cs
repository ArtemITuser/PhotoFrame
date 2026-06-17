// Services/LiveTileService.cs — v3.7 (build 40)
// Live Tiles via WinRT reflection (no hard dependency).
// Wide (310×150) and Large (310×310) tiles with photo + app name.
// Requires ClickOnce installation for tiles to appear in Start Menu.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PhotoFrame.Services
{
    public static class LiveTileService
    {
        private const string AppId = "ArtemITuser.PhotoFrame";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        public static void SetAppUserModelId()
        {
            try { SetCurrentProcessExplicitAppUserModelID(AppId); }
            catch { }
        }

        public static void UpdateTile(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return;
            try { UpdateTileInternal(imagePath); }
            catch { /* Tiles not available in this config */ }
        }

        public static void ClearTile()
        {
            try { ClearTileInternal(); }
            catch { }
        }

        // ─── WinRT via reflection ─────────────────────────────────────────────────

        private static void UpdateTileInternal(string imagePath)
        {
            // file:/// URI for local images
            string uri = "file:///" + imagePath.Replace('\\', '/');

            // Adaptive tile XML with all 4 sizes, wide/large show name
            string xml = $@"<tile>
  <visual branding=""nameAndLogo"" displayName=""PhotoFrame"">
    <binding template=""TileSmall"">
      <image src=""{uri}"" placement=""background"" hint-crop=""none""/>
    </binding>
    <binding template=""TileMedium"">
      <image src=""{uri}"" placement=""background"" hint-crop=""none""/>
    </binding>
    <binding template=""TileWide"">
      <image src=""{uri}"" placement=""background"" hint-crop=""none""/>
      <text hint-style=""captionSubtle"" hint-align=""right"">PhotoFrame</text>
    </binding>
    <binding template=""TileLarge"">
      <image src=""{uri}"" placement=""background"" hint-crop=""none""/>
      <text hint-style=""subtitle"" hint-align=""center"">PhotoFrame</text>
    </binding>
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

        private static void ClearTileInternal()
        {
            var mgr = GetUpdater();
            mgr?.GetType().GetMethod("Clear")?.Invoke(mgr, null);
        }

        private static object? GetUpdater()
        {
            var mgrType = Type.GetType(
                "Windows.UI.Notifications.TileUpdateManager, Windows, ContentType=WindowsRuntime");
            var method  = mgrType?.GetMethod("CreateTileUpdaterForApplication",
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
