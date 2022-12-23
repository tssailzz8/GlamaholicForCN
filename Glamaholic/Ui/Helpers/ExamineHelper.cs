using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace Glamaholic.Ui.Helpers {
    internal class ExamineHelper {
        private PluginUi Ui { get; }
        private string _nameInput = string.Empty;

        internal ExamineHelper(PluginUi ui) {
            this.Ui = ui;
        }

        internal unsafe void Draw() {
            if (!this.Ui.Plugin.Config.ShowExamineMenu) {
                return;
            }

            var examineAddon = (AtkUnitBase*) this.Ui.Plugin.GameGui.GetAddonByName("CharacterInspect", 1);
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (examineAddon == null || !examineAddon->IsVisible) {
                return;
            }

            HelperUtil.DrawHelper(examineAddon, "glamaholic-helper-examine", false, this.DrawDropdown);
        }

        private void DrawDropdown() {
            if (ImGui.Selectable($"Open {this.Ui.Plugin.Name}")) {
                this.Ui.OpenMainInterface();
            }

            if (ImGui.IsWindowAppearing()) {
                this._nameInput = this.Ui.Plugin.Functions.ExamineName ?? "Copied glamour";
            }

            HelperUtil.DrawCreatePlateMenu(this.Ui, GetItems, ref this._nameInput);

            if (ImGui.Selectable("Try on")) {
                var items = GetItems();
                if (items != null) {
                    this.Ui.TryOn(items.Values);
                }
            }
        }

        private static unsafe Dictionary<PlateSlot, SavedGlamourItem>? GetItems() {
            var inventory = InventoryManager.Instance()->GetInventoryContainer(InventoryType.Examine);
            if (inventory == null) {
                return null;
            }

            var items = new Dictionary<PlateSlot, SavedGlamourItem>();
            for (var i = 0; i < inventory->Size && i < (int) (PlateSlot.LeftRing + 2); i++) {
                var item = inventory->Items[i];
                var itemId = item.GlamourID;
                if (itemId == 0) {
                    itemId = item.ItemID;
                }

                if (itemId == 0) {
                    continue;
                }

                var stainId = item.Stain;

                // for some reason, this still accounts for belts in EW
                var slot = i > 5 ? i - 1 : i;
                items[(PlateSlot) slot] = new SavedGlamourItem {
                    ItemId = itemId,
                    StainId = stainId,
                };
            }

            return items;
        }
    }
}
