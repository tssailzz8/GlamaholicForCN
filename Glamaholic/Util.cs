﻿using System;
using Dalamud.Game.Gui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace Glamaholic {
    internal static class Util {
        internal const string PlateAddon = "MiragePrismMiragePlate";
        private const string BoxAddon = "MiragePrismPrismBox";
        private const string ArmoireAddon = "CabinetWithdraw";

        internal const uint HqItemOffset = 1_000_000;

        private static unsafe bool IsOpen(AtkUnitBase* addon) {
            return addon != null && addon->IsVisible;
        }

        private static unsafe bool IsOpen(GameGui gui, string name) {
            var addon = (AtkUnitBase*) gui.GetAddonByName(name, 1);
            return IsOpen(addon);
        }

        internal static bool IsEditingPlate(GameGui gui) {
            var plateOpen = IsOpen(gui, PlateAddon);
            var boxOpen = IsOpen(gui, BoxAddon);
            var armoireOpen = IsOpen(gui, ArmoireAddon);

            return plateOpen && (boxOpen || armoireOpen);
        }

        internal static bool DrawTextInput(string id, ref string input, uint max = 512, string message = "Press Enter to save.", ImGuiInputTextFlags flags = ImGuiInputTextFlags.None) {
            ImGui.SetNextItemWidth(-1);
            var ret = ImGui.InputText($"##{id}", ref input, max, ImGuiInputTextFlags.EnterReturnsTrue | flags);

            ImGui.TextUnformatted(message);

            return ret && input.Length > 0;
        }

        internal static bool IconButton(FontAwesomeIcon icon, string? id = null, string? tooltip = null, bool small = false) {
            var label = icon.ToIconString();
            if (id != null) {
                label += $"##{id}";
            }

            ImGui.PushFont(UiBuilder.IconFont);
            var ret = small
                ? ImGui.SmallButton(label)
                : ImGui.Button(label);
            ImGui.PopFont();

            if (tooltip != null && ImGui.IsItemHovered()) {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(tooltip);
                ImGui.EndTooltip();
            }

            return ret;
        }

        internal static void TextUnformattedWrapped(string text) {
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
        }

        internal static PlateSlot? GetSlot(Item item) {
            var category = item.EquipSlotCategory.Value;
            if (category == null) {
                return null;
            }

            if (category.MainHand > 0) {
                return PlateSlot.MainHand;
            }

            if (category.OffHand > 0) {
                return PlateSlot.OffHand;
            }

            if (category.Head > 0) {
                return PlateSlot.Head;
            }

            if (category.Body > 0) {
                return PlateSlot.Body;
            }

            if (category.Gloves > 0) {
                return PlateSlot.Hands;
            }

            if (category.Legs > 0) {
                return PlateSlot.Legs;
            }

            if (category.Feet > 0) {
                return PlateSlot.Feet;
            }

            if (category.Ears > 0) {
                return PlateSlot.Ears;
            }

            if (category.Neck > 0) {
                return PlateSlot.Neck;
            }

            if (category.Wrists > 0) {
                return PlateSlot.Wrists;
            }

            if (category.FingerR > 0) {
                return PlateSlot.RightRing;
            }

            if (category.FingerL > 0) {
                return PlateSlot.LeftRing;
            }

            return null;
        }

        internal static bool MatchesSlot(EquipSlotCategory category, PlateSlot slot) {
            return slot switch {
                PlateSlot.MainHand => category.MainHand > 0,
                PlateSlot.OffHand => category.OffHand > 0,
                PlateSlot.Head => category.Head > 0,
                PlateSlot.Body => category.Body > 0,
                PlateSlot.Hands => category.Gloves > 0,
                PlateSlot.Legs => category.Legs > 0,
                PlateSlot.Feet => category.Feet > 0,
                PlateSlot.Ears => category.Ears > 0,
                PlateSlot.Neck => category.Neck > 0,
                PlateSlot.Wrists => category.Wrists > 0,
                PlateSlot.RightRing => category.FingerR > 0,
                PlateSlot.LeftRing => category.FingerL > 0,
                _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
            };
        }

        internal static bool IsItemMiddleOrCtrlClicked() {
            if (ImGui.IsItemClicked(ImGuiMouseButton.Middle)) {
                return true;
            }

            return ImGui.GetIO().KeyCtrl && ImGui.IsItemClicked(ImGuiMouseButton.Left);
        }

        // https://github.com/ufx/GarlandTools/blob/5b2ec54dc792175a1d565fddb6c6b975b9a9ff64/Garland.Data/Hacks.cs#L89
        internal static bool IsItemSkipped(Item item) {
            var name = item.Name.RawString;
            return item.RowId switch {
                // Dated Radz-at-Han Coin
                17557 => false,
                // Wrapped Present (no icon)
                22357 => true,
                _ => name.Length == 0 || name.StartsWith("Dated"),
            };
        }

        internal static void TextIcon(FontAwesomeIcon icon) {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(icon.ToIconString());
            ImGui.PopFont();
        }

        internal static string GetClipboardText() {
            try {
                return ImGui.GetClipboardText();
            } catch (Exception) {
                return string.Empty;
            }
        }
    }
}
