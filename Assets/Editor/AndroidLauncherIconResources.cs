using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor.Android;
using UnityEditor.Build;

namespace ChunithmMobile.Editor
{
    // Unity otherwise emits app_icon regardless of the source texture filename.
    public sealed class AndroidLauncherIconResources : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string launcher = Path.GetFullPath(Path.Combine(path, "../launcher/src/main"));
            string resources = Path.Combine(launcher, "res");
            if (!Directory.Exists(resources)) throw new BuildFailedException("Launcher resources missing: " + resources);
            var names = new Dictionary<string, string> {
                { "app_icon", "chunithm_launcher_027_20260908" },
                { "app_icon_round", "chunithm_launcher_027_20260908_round" },
                { "ic_launcher_background", "chunithm_launcher_027_20260908_background" },
                { "ic_launcher_foreground", "chunithm_launcher_027_20260908_foreground" }
            };
            foreach (string file in Directory.GetFiles(resources, "*", SearchOption.AllDirectories)) {
                if (!names.TryGetValue(Path.GetFileNameWithoutExtension(file), out string name)) continue;
                string target = Path.Combine(Path.GetDirectoryName(file), name + Path.GetExtension(file));
                File.Copy(file, target, true);
                File.Delete(file);
            }
            foreach (string file in Directory.GetFiles(resources, "*.xml", SearchOption.AllDirectories)
                .Concat(new[] { Path.Combine(launcher, "AndroidManifest.xml") })) {
                XDocument xml = XDocument.Load(file);
                bool changed = false;
                foreach (XAttribute attribute in xml.Descendants().Attributes()) {
                    foreach (var pair in names) {
                        if (attribute.Value != "@mipmap/" + pair.Key) continue;
                        attribute.Value = "@mipmap/" + pair.Value;
                        changed = true;
                        break;
                    }
                }
                if (changed) xml.Save(file);
            }
        }
    }
}
