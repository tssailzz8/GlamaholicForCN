using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace Glamaholic.Ui.Helpers {
    internal class EditorHelper {
        private PluginUi Ui { get; }
        private string _plateName = string.Empty;

        internal EditorHelper(PluginUi ui) {
            this.Ui = ui;
        }

        internal unsafe void Draw() {
            if (!this.Ui.Plugin.Config.ShowEditorMenu || !Util.IsEditingPlate(this.Ui.Plugin.GameGui)) {
                return;
            }

            var addon = (AtkUnitBase*) this.Ui.Plugin.GameGui.GetAddonByName(Util.PlateAddon, 1);
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (addon == null || !addon->IsVisible) {
                return;
            }

            HelperUtil.DrawHelper(addon, "glamaholic-editor-helper", false, this.DrawDropdown);
        }

        private void DrawDropdown() {
            if (ImGui.Selectable($"Open {this.Ui.Plugin.Name}")) {
                this.Ui.OpenMainInterface();
            }

            if (HelperUtil.DrawCreatePlateMenu(this.Ui, () => GameFunctions.CurrentPlate, ref this._plateName)) {
                this._plateName = string.Empty;
            }
        }
    }
}
