﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Logging;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using Newtonsoft.Json;

namespace Glamaholic.Ui {
    internal class MainInterface {
        internal const int IconSize = 48;

        private static readonly PlateSlot[] LeftSide = {
            PlateSlot.MainHand,
            PlateSlot.Head,
            PlateSlot.Body,
            PlateSlot.Hands,
            PlateSlot.Legs,
            PlateSlot.Feet,
        };

        private static readonly PlateSlot[] RightSide = {
            PlateSlot.OffHand,
            PlateSlot.Ears,
            PlateSlot.Neck,
            PlateSlot.Wrists,
            PlateSlot.RightRing,
            PlateSlot.LeftRing,
        };

        private PluginUi Ui { get; }
        private List<Item> Items { get; }
        private List<Item> FilteredItems { get; set; }
        private Dictionary<string, byte> Stains { get; }

        private FilterInfo? PlateFilter { get; set; }

        private bool _visible;
        private int _dragging = -1;
        private int _selectedPlate = -1;
        private bool _scrollToSelected;
        private string _plateFilter = string.Empty;
        private bool _showRename;
        private string _renameInput = string.Empty;
        private bool _deleteConfirm;
        private bool _editing;
        private SavedPlate? _editingPlate;
        private string _itemFilter = string.Empty;
        private string _dyeFilter = string.Empty;
        private volatile bool _ecImporting;
        private readonly Dictionary<string, Stopwatch> _timedMessages = new();
        private string _tagInput = string.Empty;

        internal MainInterface(PluginUi ui) {
            this.Ui = ui;

            // get all equippable items that aren't soul crystals
            this.Items = this.Ui.Plugin.DataManager.GetExcelSheet<Item>(ClientLanguage.ChineseSimplified)!
                .Where(row => row.EquipSlotCategory.Row is not 0 && row.EquipSlotCategory.Value!.SoulCrystal == 0)
                .ToList();
            this.FilteredItems = this.Items;

            this.Stains = this.Ui.Plugin.DataManager.GetExcelSheet<Stain>(ClientLanguage.ChineseSimplified)!
                .Where(row => row.RowId != 0)
                .Where(row => !string.IsNullOrWhiteSpace(row.Name.RawString))
                .ToDictionary(row => row.Name.RawString, row => (byte) row.RowId);
        }

        internal void Open() {
            this._visible = true;
        }

        internal void Toggle() {
            this._visible ^= true;
        }

        internal void Draw() {
            this.HandleTimers();

            if (!this._visible) {
                return;
            }

            ImGui.SetNextWindowSize(new Vector2(415, 650), ImGuiCond.FirstUseEver);

            if (!ImGui.Begin(this.Ui.Plugin.Name, ref this._visible, ImGuiWindowFlags.MenuBar)) {
                ImGui.End();
                return;
            }

            this.DrawInner();

            ImGui.End();
        }

        private static bool IsValidEorzeaCollectionUrl(string urlString) {
            if (!Uri.TryCreate(urlString, UriKind.Absolute, out var url)) {
                return false;
            }

            return url.Host == "ffxiv.eorzeacollection.com" && url.AbsolutePath.StartsWith("/glamour/");
        }

        private void DrawMenuBar() {
            if (!ImGui.BeginMenuBar()) {
                return;
            }

            if (ImGui.BeginMenu("Plates")) {
                if (ImGui.MenuItem("New")) {
                    this.Ui.Plugin.Config.AddPlate(new SavedPlate("Untitled Plate"));
                    this.Ui.Plugin.SaveConfig();
                    this.SwitchPlate(this.Ui.Plugin.Config.Plates.Count - 1, true);
                }

                if (ImGui.BeginMenu("Import")) {
                    if (ImGui.MenuItem("Clipboard")) {
                        var json = Util.GetClipboardText();
                        try {
                            var plate = JsonConvert.DeserializeObject<SharedPlate>(json);
                            if (plate != null) {
                                this.Ui.Plugin.Config.AddPlate(plate.ToPlate());
                                this.Ui.Plugin.SaveConfig();
                                this.Ui.SwitchPlate(this.Ui.Plugin.Config.Plates.Count - 1);
                            }
                        } catch (Exception ex) {
                            PluginLog.LogWarning(ex, "Failed to import glamour plate");
                        }
                    }

                    var validUrl = IsValidEorzeaCollectionUrl(Util.GetClipboardText());
                    if (ImGui.MenuItem("Copied Eorzea Collection URL", validUrl) && !this._ecImporting) {
                        this.ImportEorzeaCollection(Util.GetClipboardText());
                    }

                    ImGui.EndMenu();
                }

                ImGui.EndMenu();
            }

            var anyChanged = false;
            if (ImGui.BeginMenu("Settings")) {
                anyChanged |= ImGui.MenuItem("Show plate editor menu", null, ref this.Ui.Plugin.Config.ShowEditorMenu);
                anyChanged |= ImGui.MenuItem("Show examine window menu", null, ref this.Ui.Plugin.Config.ShowExamineMenu);
                anyChanged |= ImGui.MenuItem("Show try on menu", null, ref this.Ui.Plugin.Config.ShowTryOnMenu);
                ImGui.Separator();
                anyChanged |= ImGui.MenuItem("Show Ko-fi button", null, ref this.Ui.Plugin.Config.ShowKofiButton);

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Help")) {
                foreach (var (title, content) in this.Ui.Help) {
                    if (!ImGui.BeginMenu(title)) {
                        continue;
                    }

                    ImGui.PushTextWrapPos(ImGui.CalcTextSize("0").X * 60f * ImGuiHelpers.GlobalScale);
                    ImGui.TextUnformatted(content);
                    ImGui.PopTextWrapPos();

                    ImGui.EndMenu();
                }

                ImGui.EndMenu();
            }

            if (this.Ui.Plugin.Config.ShowKofiButton) {
                const string kofiText = "Support on Ko-fi";
                var kofiTextSize = ImGui.CalcTextSize(kofiText);
                ImGui.GetWindowDrawList().AddRectFilled(
                    ImGui.GetCursorScreenPos(),
                    ImGui.GetCursorScreenPos() + kofiTextSize + ImGui.GetStyle().ItemInnerSpacing * 2,
                    0xFF5B5EFF
                );
                ImGui.PushStyleColor(ImGuiCol.Text, 0xFFFFFFFF);
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0x00000000);
                if (ImGui.MenuItem(kofiText)) {
                    Process.Start(new ProcessStartInfo("https://ko-fi.com/ascclemens") {
                        UseShellExecute = true,
                    });
                }

                ImGui.PopStyleColor(2);
            }

            if (anyChanged) {
                this.Ui.Plugin.SaveConfig();
            }

            ImGui.EndMenuBar();
        }

        private void ImportEorzeaCollection(string url) {
            if (!IsValidEorzeaCollectionUrl(url)) {
                return;
            }

            this._ecImporting = true;

            Task.Run(async () => {
                var items = new Dictionary<PlateSlot, SavedGlamourItem>();

                var client = new HttpClient();
                var resp = await client.GetAsync(url);
                var html = await resp.Content.ReadAsStringAsync();

                var titleParts = html.Split("<title>");
                var glamName = titleParts.Length > 1
                    ? WebUtility.HtmlDecode(titleParts[1].Split('<')[0].Split('|')[0].Trim())
                    : "Eorzea Collection plate";

                var parts = html.Split("c-gear-slot-item-name");
                foreach (var part in parts) {
                    var nameParts = part.Split('>');
                    if (nameParts.Length < 2) {
                        continue;
                    }

                    var rawName = nameParts[1].Split('<')[0].Trim();
                    var name = WebUtility.HtmlDecode(rawName);
                    if (string.IsNullOrWhiteSpace(name)) {
                        continue;
                    }

                    var item = this.Items.Find(item => item.Name == name);
                    if (item == null) {
                        continue;
                    }

                    var slot = Util.GetSlot(item);
                    if (slot is PlateSlot.RightRing && items.ContainsKey(PlateSlot.RightRing)) {
                        slot = PlateSlot.LeftRing;
                    }

                    if (slot == null) {
                        continue;
                    }

                    var stainId = item.IsDyeable ? this.GetStainIdFromPart(part) : (byte) 0;
                    items[slot.Value] = new SavedGlamourItem {
                        ItemId = item.RowId,
                        StainId = stainId,
                    };
                }

                this._ecImporting = false;

                var plate = new SavedPlate(glamName) {
                    Items = items,
                };
                this.Ui.Plugin.Config.AddPlate(plate);
                this.Ui.Plugin.SaveConfig();
                this.SwitchPlate(this.Ui.Plugin.Config.Plates.Count - 1, true);
            });
        }

        private byte GetStainIdFromPart(string part) {
            var stainParts = part.Split('⬤');
            if (stainParts.Length <= 1) {
                return 0;
            }

            var stainSubParts = stainParts[1].Split('>');
            if (stainSubParts.Length <= 1) {
                return 0;
            }

            var rawStainName = stainSubParts[1].Split('<')[0].Trim();
            var stainName = WebUtility.HtmlDecode(rawStainName);
            this.Stains.TryGetValue(stainName, out var stainId);
            return stainId;
        }

        private void DrawPlateList() {
            if (!ImGui.BeginChild("plate list", new Vector2(205 * ImGuiHelpers.GlobalScale, 0), true)) {
                return;
            }

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##plate-filter", "Search...", ref this._plateFilter, 512, ImGuiInputTextFlags.AutoSelectAll)) {
                this.PlateFilter = this._plateFilter.Length == 0
                    ? null
                    : new FilterInfo(this.Ui.Plugin.DataManager, this._plateFilter);
            }

            (int src, int dst)? drag = null;
            if (ImGui.BeginChild("plate list actual", Vector2.Zero, false, ImGuiWindowFlags.HorizontalScrollbar)) {
                for (var i = 0; i < this.Ui.Plugin.Config.Plates.Count; i++) {
                    var plate = this.Ui.Plugin.Config.Plates[i];

                    if (this.PlateFilter != null && !this.PlateFilter.Matches(plate)) {
                        continue;
                    }

                    int? switchTo = null;
                    if (ImGui.Selectable($"{plate.Name}##{i}", this._selectedPlate == i)) {
                        switchTo = i;
                    }

                    if (this._scrollToSelected && this._selectedPlate == i) {
                        this._scrollToSelected = false;
                        ImGui.SetScrollHereY(1f);
                    }

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
                        switchTo = -1;
                    }

                    if (ImGui.IsItemHovered()) {
                        ImGui.PushFont(UiBuilder.IconFont);
                        var deleteWidth = ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X;
                        ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemInnerSpacing.X * 2 - deleteWidth);
                        ImGui.TextUnformatted(FontAwesomeIcon.Times.ToIconString());
                        ImGui.PopFont();

                        var mouseDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
                        var mouseClicked = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
                        if (ImGui.IsItemHovered() || mouseDown) {
                            if (mouseClicked) {
                                switchTo = null;

                                if (this._deleteConfirm) {
                                    this._deleteConfirm = false;
                                    if (this._selectedPlate == i) {
                                        switchTo = -1;
                                    }

                                    this.Ui.Plugin.Config.Plates.RemoveAt(i);
                                    this.Ui.Plugin.SaveConfig();
                                } else {
                                    this._deleteConfirm = true;
                                }
                            }
                        } else {
                            this._deleteConfirm = false;
                        }

                        if (this._deleteConfirm) {
                            ImGui.BeginTooltip();
                            ImGui.TextUnformatted("Click delete again to confirm.");
                            ImGui.EndTooltip();
                        }
                    }

                    if (switchTo != null) {
                        this.SwitchPlate(switchTo.Value);
                    }

                    // handle dragging
                    if (this._plateFilter.Length == 0 && ImGui.IsItemActive() || this._dragging == i) {
                        this._dragging = i;
                        var step = 0;
                        if (ImGui.GetIO().MouseDelta.Y < 0 && ImGui.GetMousePos().Y < ImGui.GetItemRectMin().Y) {
                            step = -1;
                        }

                        if (ImGui.GetIO().MouseDelta.Y > 0 && ImGui.GetMousePos().Y > ImGui.GetItemRectMax().Y) {
                            step = 1;
                        }

                        if (step != 0) {
                            drag = (i, i + step);
                        }
                    }
                }

                if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && this._dragging != -1) {
                    this._dragging = -1;
                    this.Ui.Plugin.SaveConfig();
                }

                if (drag != null && drag.Value.dst < this.Ui.Plugin.Config.Plates.Count && drag.Value.dst >= 0) {
                    this._dragging = drag.Value.dst;
                    // ReSharper disable once SwapViaDeconstruction
                    var temp = this.Ui.Plugin.Config.Plates[drag.Value.src];
                    this.Ui.Plugin.Config.Plates[drag.Value.src] = this.Ui.Plugin.Config.Plates[drag.Value.dst];
                    this.Ui.Plugin.Config.Plates[drag.Value.dst] = temp;

                    // do not SwitchPlate, because this is technically not a switch
                    if (this._selectedPlate == drag.Value.dst) {
                        var step = drag.Value.dst - drag.Value.src;
                        this._selectedPlate = drag.Value.dst - step;
                    } else if (this._selectedPlate == drag.Value.src) {
                        this._selectedPlate = drag.Value.dst;
                    }
                }

                ImGui.EndChild();
            }

            ImGui.EndChild();
        }

        private void DrawDyePopup(string dyePopup, SavedGlamourItem mirage) {
            if (!ImGui.BeginPopup(dyePopup)) {
                return;
            }

            ImGui.PushItemWidth(-1);
            ImGui.InputText("##dye-filter", ref this._dyeFilter, 512);

            if (ImGui.IsWindowAppearing()) {
                ImGui.SetKeyboardFocusHere(-1);
            }

            if (ImGui.BeginChild("dye picker", new Vector2(250, 350), false, ImGuiWindowFlags.HorizontalScrollbar)) {
                if (ImGui.Selectable("None", mirage.StainId == 0)) {
                    mirage.StainId = 0;
                    ImGui.CloseCurrentPopup();
                }

                var filter = this._dyeFilter.ToLowerInvariant();

                foreach (var stain in this.Ui.Plugin.DataManager.GetExcelSheet<Stain>()!) {
                    if (stain.RowId == 0 || stain.Shade == 0) {
                        continue;
                    }

                    if (filter.Length > 0 && !stain.Name.RawString.ToLowerInvariant().Contains(filter)) {
                        continue;
                    }

                    if (ImGui.Selectable($"{stain.Name}##{stain.RowId}", mirage.StainId == stain.RowId)) {
                        mirage.StainId = (byte) stain.RowId;
                        ImGui.CloseCurrentPopup();
                    }
                }

                ImGui.EndChild();
            }

            ImGui.EndPopup();
        }

        private unsafe void DrawItemPopup(string itemPopup, SavedPlate plate, PlateSlot slot) {
            if (!ImGui.BeginPopup(itemPopup)) {
                return;
            }

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##item-filter", "Search...", ref this._itemFilter, 512, ImGuiInputTextFlags.AutoSelectAll)) {
                this.FilterItems(slot);
            }

            if (ImGui.IsWindowAppearing()) {
                ImGui.SetKeyboardFocusHere(-1);
            }

            if (GameFunctions.DresserContents.Count > 0) {
                if (ImGui.Checkbox("Only show items in the armoire/dresser", ref this.Ui.Plugin.Config.ItemFilterShowObtainedOnly)) {
                    this.Ui.Plugin.SaveConfig();
                    this.FilterItems(slot);
                }

                ImGui.Separator();
            }

            if (ImGui.BeginChild("item search", new Vector2(250, 450), false, ImGuiWindowFlags.HorizontalScrollbar)) {
                uint? id;
                if (plate.Items.TryGetValue(slot, out var slotMirage)) {
                    id = slotMirage.ItemId;
                } else {
                    id = null;
                }

                if (ImGui.Selectable("##none-keep", id == null)) {
                    plate.Items.Remove(slot);
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                Util.TextIcon(FontAwesomeIcon.Box);
                ImGui.SameLine();
                ImGui.TextUnformatted("None (keep existing)");

                if (ImGui.Selectable("##none-remove)", id == 0)) {
                    plate.Items[slot] = new SavedGlamourItem();
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                Util.TextIcon(FontAwesomeIcon.Box);
                ImGui.SameLine();
                ImGui.TextUnformatted("None (remove existing)");

                var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());

                clipper.Begin(this.FilteredItems.Count);
                while (clipper.Step()) {
                    for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++) {
                        var item = this.FilteredItems[i];

                        if (ImGui.Selectable($"##{item.RowId}", item.RowId == id)) {
                            if (!plate.Items.ContainsKey(slot)) {
                                plate.Items[slot] = new SavedGlamourItem();
                            }

                            plate.Items[slot].ItemId = item.RowId;
                            if (!item.IsDyeable) {
                                plate.Items[slot].StainId = 0;
                            }

                            ImGui.CloseCurrentPopup();
                        }

                        if (Util.IsItemMiddleOrCtrlClicked()) {
                            this.Ui.AlternativeFinders.Add(new AlternativeFinder(this.Ui, item));
                        }

                        ImGui.SameLine();

                        var has = GameFunctions.DresserContents.Any(saved => saved.ItemId % Util.HqItemOffset == item.RowId) || this.Ui.Plugin.Functions.IsInArmoire(item.RowId);

                        if (!has) {
                            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int) ImGuiCol.TextDisabled]);
                        }

                        Util.TextIcon(FontAwesomeIcon.Box);

                        if (!has) {
                            ImGui.PopStyleColor();
                        }

                        ImGui.SameLine();

                        ImGui.TextUnformatted($"{item.Name}");
                    }
                }

                ImGui.EndChild();
            }

            ImGui.EndPopup();
        }

        private unsafe void DrawIcon(PlateSlot slot, SavedPlate plate, int iconSize, int paddingSize) {
            var drawCursor = ImGui.GetCursorScreenPos();
            var tooltip = slot.Name();
            ImGui.BeginGroup();

            plate.Items.TryGetValue(slot, out var mirage);

            var borderColour = *ImGui.GetStyleColorVec4(ImGuiCol.Border);

            // check for item
            if (mirage != null && mirage.ItemId != 0 && GameFunctions.DresserContents.Count > 0) {
                var has = GameFunctions.DresserContents.Any(saved => saved.ItemId % Util.HqItemOffset == mirage.ItemId) || this.Ui.Plugin.Functions.IsInArmoire(mirage.ItemId);
                if (!has) {
                    borderColour = ImGuiColors.DalamudYellow;
                }
            }

            ImGui.GetWindowDrawList().AddRect(drawCursor, drawCursor + new Vector2(iconSize + paddingSize), ImGui.ColorConvertFloat4ToU32(borderColour));

            var cursorBefore = ImGui.GetCursorPos();
            ImGui.InvisibleButton($"preview {slot}", new Vector2(iconSize + paddingSize));
            var cursorAfter = ImGui.GetCursorPos();

            if (mirage != null && mirage.ItemId != 0) {
                var item = this.Ui.Plugin.DataManager.GetExcelSheet<Item>()!.GetRow(mirage.ItemId);
                if (item != null) {
                    var icon = this.Ui.GetIcon(item.Icon);
                    if (icon != null) {
                        ImGui.SetCursorPos(cursorBefore + new Vector2(paddingSize / 2f));
                        ImGui.Image(icon.ImGuiHandle, new Vector2(iconSize));
                        ImGui.SetCursorPos(cursorAfter);

                        var stain = this.Ui.Plugin.DataManager.GetExcelSheet<Stain>()!.GetRow(mirage.StainId);
                        var circleCentre = drawCursor + new Vector2(iconSize, 4 + paddingSize / 2f);
                        if (mirage.StainId != 0 && stain != null) {
                            var colour = stain.Color;
                            var abgr = 0xFF000000;
                            abgr |= (colour & 0xFF) << 16;
                            abgr |= ((colour >> 8) & 0xFF) << 8;
                            abgr |= (colour >> 16) & 0xFF;
                            ImGui.GetWindowDrawList().AddCircleFilled(circleCentre, 4, abgr);
                        }

                        if (item.IsDyeable) {
                            ImGui.GetWindowDrawList().AddCircle(circleCentre, 5, 0xFF000000);
                        }

                        var stainName = mirage.StainId == 0 || stain == null
                            ? ""
                            : $" ({stain.Name})";
                        tooltip += $"\n{item.Name}{stainName}";
                    }
                }
            } else if (mirage != null) {
                // remove
                ImGui.GetWindowDrawList().AddLine(
                    drawCursor + new Vector2(paddingSize / 2f),
                    drawCursor + new Vector2(paddingSize / 2f) + new Vector2(iconSize),
                    ImGui.ColorConvertFloat4ToU32(ImGui.GetStyle().Colors[(int) ImGuiCol.Text])
                );

                ImGui.GetWindowDrawList().AddLine(
                    drawCursor + new Vector2(paddingSize / 2f) + new Vector2(iconSize, 0),
                    drawCursor + new Vector2(paddingSize / 2f) + new Vector2(0, iconSize),
                    ImGui.ColorConvertFloat4ToU32(ImGui.GetStyle().Colors[(int) ImGuiCol.Text])
                );
            }

            ImGui.EndGroup();

            // fix spacing
            ImGui.SetCursorPos(cursorAfter);

            if (ImGui.IsItemHovered()) {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(tooltip);
                ImGui.EndTooltip();
            }

            var itemPopup = $"plate item edit {slot}";
            var dyePopup = $"plate item dye {slot}";
            if (this._editing && ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                ImGui.OpenPopup(itemPopup);
                this.FilterItems(slot);
            }

            if (this._editing && ImGui.IsItemClicked(ImGuiMouseButton.Right) && mirage != null) {
                var dyeable = this.Ui.Plugin.DataManager.GetExcelSheet<Item>()!.GetRow(mirage.ItemId)?.IsDyeable ?? false;
                if (dyeable) {
                    ImGui.OpenPopup(dyePopup);
                }
            }

            if (mirage != null && mirage.ItemId != 0 && Util.IsItemMiddleOrCtrlClicked()) {
                var item = this.Ui.Plugin.DataManager.GetExcelSheet<Item>()!.GetRow(mirage.ItemId);
                if (item != null) {
                    this.Ui.AlternativeFinders.Add(new AlternativeFinder(this.Ui, item));
                }
            }

            this.DrawItemPopup(itemPopup, plate, slot);

            if (mirage != null) {
                this.DrawDyePopup(dyePopup, mirage);
            }
        }

        private void DrawPlatePreview(SavedPlate plate) {
            const int paddingSize = 12;

            if (!ImGui.BeginTable("plate item preview", 2, ImGuiTableFlags.SizingFixedFit)) {
                return;
            }

            foreach (var (left, right) in LeftSide.Zip(RightSide)) {
                ImGui.TableNextColumn();
                this.DrawIcon(left, plate, IconSize, paddingSize);
                ImGui.TableNextColumn();
                this.DrawIcon(right, plate, IconSize, paddingSize);
            }

            ImGui.EndTable();
        }

        private void DrawPlateButtons(SavedPlate plate) {
            if (this._editing || !ImGui.BeginTable("plate buttons", 5, ImGuiTableFlags.SizingFixedFit)) {
                return;
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.Check, tooltip: "Apply")) {
                if (!Util.IsEditingPlate(this.Ui.Plugin.GameGui)) {
                    this.AddTimedMessage("The in-game plate editor must be open.");
                } else {
                    this.Ui.Plugin.Functions.LoadPlate(plate);
                }
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.Search, tooltip: "Try on")) {
                this.Ui.TryOn(plate.Items.Values);
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.Font, tooltip: "Rename")) {
                this._showRename ^= true;
                this._renameInput = plate.Name;
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.PencilAlt, tooltip: "Edit")) {
                this._editing = true;
                this._editingPlate = plate.Clone();
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.ShareAltSquare, tooltip: "Share")) {
                ImGui.SetClipboardText(JsonConvert.SerializeObject(new SharedPlate(plate)));
                this.AddTimedMessage("Copied to clipboard.");
            }

            ImGui.EndTable();
        }

        private void DrawPlateTags(SavedPlate plate) {
            if (this._editing) {
                return;
            }

            if (!ImGui.CollapsingHeader($"Tags ({plate.Tags.Count})###plate-tags")) {
                return;
            }

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##tag-input", "Input a tag and press Enter", ref this._tagInput, 128, ImGuiInputTextFlags.EnterReturnsTrue)) {
                if (!string.IsNullOrWhiteSpace(this._tagInput)) {
                    var tag = this._tagInput.Trim();

                    if (!plate.Tags.Contains(tag)) {
                        plate.Tags.Add(tag);
                        plate.Tags.Sort();
                        this.Ui.Plugin.SaveConfig();
                    }
                }

                this._tagInput = string.Empty;
            }

            if (ImGui.BeginChild("tag-list")) {
                var toRemove = -1;
                for (var i = 0; i < plate.Tags.Count; i++) {
                    var tag = plate.Tags[i];

                    if (Util.IconButton(FontAwesomeIcon.Times, $"remove-tag-{i}")) {
                        toRemove = i;
                    }

                    ImGui.SameLine();
                    ImGui.TextUnformatted(tag);
                }

                if (toRemove > -1) {
                    plate.Tags.RemoveAt(toRemove);
                    this.Ui.Plugin.SaveConfig();
                }

                ImGui.EndChild();
            }
        }

        private void DrawPlateDetail() {
            if (!ImGui.BeginChild("plate detail")) {
                return;
            }

            if (this._selectedPlate > -1 && this._selectedPlate < this.Ui.Plugin.Config.Plates.Count) {
                var plate = this._editingPlate ?? this.Ui.Plugin.Config.Plates[this._selectedPlate];

                this.DrawPlatePreview(plate);

                var renameWasVisible = this._showRename;

                this.DrawPlateButtons(plate);

                foreach (var (msg, _) in this._timedMessages) {
                    Util.TextUnformattedWrapped(msg);
                }

                if (this._showRename && Util.DrawTextInput("plate-rename", ref this._renameInput, flags: ImGuiInputTextFlags.AutoSelectAll)) {
                    plate.Name = this._renameInput;
                    this.Ui.Plugin.SaveConfig();
                    this._showRename = false;
                }

                if (this._showRename && !renameWasVisible) {
                    ImGui.SetKeyboardFocusHere(-1);
                }

                if (this._editing) {
                    Util.TextUnformattedWrapped("Click an item to edit it. Right-click to dye.");

                    if (ImGui.Button("Save") && this._editingPlate != null) {
                        this.Ui.Plugin.Config.Plates[this._selectedPlate] = this._editingPlate;
                        this.Ui.Plugin.SaveConfig();
                        this.ResetEditing();
                    }

                    ImGui.SameLine();

                    if (ImGui.Button("Cancel")) {
                        this.ResetEditing();
                    }
                }

                this.DrawPlateTags(plate);
            }

            ImGui.EndChild();
        }

        private void DrawWarnings() {
            var warnings = new List<string>();

            if (!this.Ui.Plugin.Functions.ArmoireLoaded) {
                warnings.Add("The Armoire is not loaded. Open it once to enable glamours from the Armoire.");
            }

            if (GameFunctions.DresserContents.Count == 0) {
                warnings.Add("Glamour Dresser is empty or has not been opened. Glamaholic will not know which items you have.");
            }

            if (warnings.Count == 0) {
                return;
            }

            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            var header = ImGui.CollapsingHeader($"Warnings ({warnings.Count})###warnings");
            ImGui.PopStyleColor();

            if (!header) {
                return;
            }

            for (var i = 0; i < warnings.Count; i++) {
                if (i != 0) {
                    ImGui.Separator();
                }

                Util.TextUnformattedWrapped(warnings[i]);
            }
        }

        private void DrawInner() {
            this.DrawMenuBar();

            this.DrawWarnings();

            this.DrawPlateList();

            ImGui.SameLine();

            this.DrawPlateDetail();

            ImGui.End();
        }

        private void HandleTimers() {
            var keys = this._timedMessages.Keys.ToArray();
            foreach (var key in keys) {
                if (this._timedMessages[key].Elapsed > TimeSpan.FromSeconds(5)) {
                    this._timedMessages.Remove(key);
                }
            }
        }

        private void AddTimedMessage(string message) {
            var timer = new Stopwatch();
            timer.Start();
            this._timedMessages[message] = timer;
        }

        internal void SwitchPlate(int idx, bool scrollTo = false) {
            this._selectedPlate = idx;
            this._scrollToSelected = scrollTo;
            this._renameInput = string.Empty;
            this._showRename = false;
            this._deleteConfirm = false;
            this._timedMessages.Clear();
            this.ResetEditing();
        }

        private void ResetEditing() {
            this._editing = false;
            this._editingPlate = null;
            this._itemFilter = string.Empty;
            this._dyeFilter = string.Empty;
        }

        private void FilterItems(PlateSlot slot) {
            var filter = this._itemFilter.ToLowerInvariant();

            IEnumerable<Item> items;
            if (GameFunctions.DresserContents.Count > 0 && this.Ui.Plugin.Config.ItemFilterShowObtainedOnly) {
                var sheet = this.Ui.Plugin.DataManager.GetExcelSheet<Item>()!;
                items = GameFunctions.DresserContents
                    .Select(item => sheet.GetRow(item.ItemId))
                    .Where(item => item != null)
                    .Cast<Item>();
            } else {
                items = this.Items;
            }

            this.FilteredItems = items
                .Where(item => !Util.IsItemSkipped(item))
                .Where(item => Util.MatchesSlot(item.EquipSlotCategory.Value!, slot))
                .Where(item => this._itemFilter.Length == 0 || item.Name.RawString.ToLowerInvariant().Contains(filter))
                .ToList();
        }
    }
}
