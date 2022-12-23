using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Glamaholic.Ui;
using Glamaholic.Ui.Helpers;
using ImGuiScene;

namespace Glamaholic {
    internal class PluginUi : IDisposable {
        internal Plugin Plugin { get; }

        private Dictionary<ushort, TextureWrap> Icons { get; } = new();

        private MainInterface MainInterface { get; }
        private EditorHelper EditorHelper { get; }
        private ExamineHelper ExamineHelper { get; }
        private TryOnHelper TryOnHelper { get; }
        internal List<AlternativeFinder> AlternativeFinders { get; } = new();
        internal List<(string, string)> Help { get; } = new();

        internal PluginUi(Plugin plugin) {
            this.Plugin = plugin;

            foreach (var entry in Resourcer.Resource.AsString("help.txt").Split("---")) {
                var lines = entry.Trim().Split(new[] { "\n", "\r\n" }, StringSplitOptions.TrimEntries);
                if (lines.Length == 0 || !lines[0].StartsWith("#")) {
                    continue;
                }

                var title = lines[0][1..].Trim();
                var content = string.Join(" ", lines[1..]
                    .SkipWhile(string.IsNullOrWhiteSpace)
                    .Select(line => string.IsNullOrWhiteSpace(line) ? "\n" : line));
                this.Help.Add((title, content));
            }

            this.MainInterface = new MainInterface(this);
            this.EditorHelper = new EditorHelper(this);
            this.ExamineHelper = new ExamineHelper(this);
            this.TryOnHelper = new TryOnHelper(this);

            this.Plugin.Interface.UiBuilder.Draw += this.Draw;
            this.Plugin.Interface.UiBuilder.OpenConfigUi += this.OpenMainInterface;
        }

        public void Dispose() {
            this.Plugin.Interface.UiBuilder.OpenConfigUi -= this.OpenMainInterface;
            this.Plugin.Interface.UiBuilder.Draw -= this.Draw;

            foreach (var icon in this.Icons.Values) {
                icon.Dispose();
            }
        }

        internal void OpenMainInterface() {
            this.MainInterface.Open();
        }

        internal void ToggleMainInterface() {
            this.MainInterface.Toggle();
        }

        internal TextureWrap? GetIcon(ushort id) {
            if (this.Icons.TryGetValue(id, out var cached)) {
                return cached;
            }

            var icon = this.Plugin.DataManager.GetImGuiTextureIcon(id);
            if (icon == null) {
                return null;
            }

            this.Icons[id] = icon;
            return icon;
        }

        private void Draw() {
            this.MainInterface.Draw();
            this.EditorHelper.Draw();
            this.ExamineHelper.Draw();
            this.TryOnHelper.Draw();

            this.AlternativeFinders.RemoveAll(finder => {
                finder.Draw();
                return !finder.Visible;
            });
        }

        internal void SwitchPlate(int idx, bool scrollTo = false) {
            this.MainInterface.SwitchPlate(idx, scrollTo);
        }

        internal unsafe void TryOn(IEnumerable<SavedGlamourItem> items) {
            void SetTryOnSave(bool save) {
                var tryOnAgent = (IntPtr) Framework.Instance()->GetUiModule()->GetAgentModule()->GetAgentByInternalId(AgentId.Tryon);
                if (tryOnAgent != IntPtr.Zero) {
                    *(byte*) (tryOnAgent + 0x2E2) = (byte) (save ? 1 : 0);
                }
            }

            SetTryOnSave(false);
            foreach (var mirage in items) {
                if (mirage.ItemId == 0) {
                    continue;
                }

                this.Plugin.Functions.TryOn(mirage.ItemId, mirage.StainId);
                SetTryOnSave(true);
            }
        }
    }
}
