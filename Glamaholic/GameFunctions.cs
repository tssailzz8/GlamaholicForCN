using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Memory;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.GeneratedSheets;

namespace Glamaholic {
    internal class GameFunctions : IDisposable {
        private static class Signatures {
            internal const string SetGlamourPlateSlot = "E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 48 8B 46 10";
            internal const string ModifyGlamourPlateSlot = "48 89 74 24 ?? 57 48 83 EC 20 80 79 30 00";
            internal const string ClearGlamourPlateSlot = "80 79 30 00 4C 8B C1";
            internal const string IsInArmoire = "E8 ?? ?? ?? ?? 84 C0 74 16 8B CB";
            internal const string ArmoirePointer = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 84 C0 74 16 8B CB E8";
            internal const string TryOn = "E8 ?? ?? ?? ?? EB 35 BA";
            internal const string ExamineNamePointer = "48 8D 05 ?? ?? ?? ?? 48 89 85 ?? ?? ?? ?? 74 56 49 8B 4F";
        }

        #region Delegates

        private delegate void SetGlamourPlateSlotDelegate(IntPtr agent, MirageSource mirageSource, int glamId, uint itemId, byte stainId);

        private delegate void ModifyGlamourPlateSlotDelegate(IntPtr agent, PlateSlot slot, byte stainId, IntPtr numbers, int stainItemId);

        private delegate void ClearGlamourPlateSlotDelegate(IntPtr agent, PlateSlot slot);

        private delegate byte IsInArmoireDelegate(IntPtr armoire, int index);

        private delegate byte TryOnDelegate(uint unknownCanEquip, uint itemBaseId, ulong stainColor, uint itemGlamourId, byte unknownByte);

        #endregion

        private Plugin Plugin { get; }

        #region Functions

        [Signature(Signatures.SetGlamourPlateSlot)]
        private readonly SetGlamourPlateSlotDelegate _setGlamourPlateSlot = null!;

        [Signature(Signatures.ModifyGlamourPlateSlot)]
        private readonly ModifyGlamourPlateSlotDelegate _modifyGlamourPlateSlot = null!;

        [Signature(Signatures.ClearGlamourPlateSlot)]
        private readonly ClearGlamourPlateSlotDelegate _clearGlamourPlateSlot = null!;

        [Signature(Signatures.IsInArmoire)]
        private readonly IsInArmoireDelegate _isInArmoire = null!;

        [Signature(Signatures.ArmoirePointer, ScanType = ScanType.StaticAddress)]
        private readonly IntPtr _armoirePtr;

        [Signature(Signatures.TryOn)]
        private readonly TryOnDelegate _tryOn = null!;

        [Signature(Signatures.ExamineNamePointer, ScanType = ScanType.StaticAddress)]
        private readonly IntPtr _examineNamePtr;

        #endregion

        private readonly List<uint> _filterIds = new();

        internal GameFunctions(Plugin plugin) {
            this.Plugin = plugin;

            SignatureHelper.Initialise(this);

            this.Plugin.ChatGui.ChatMessage += this.OnChat;
            this.Plugin.ClientState.Login += OnLogin;
            this.Plugin.Framework.Update += this.OnFrameworkUpdate;
        }

        public void Dispose() {
            this.Plugin.Framework.Update -= this.OnFrameworkUpdate;
            this.Plugin.ClientState.Login -= OnLogin;
            this.Plugin.ChatGui.ChatMessage -= this.OnChat;
        }

        private void OnChat(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled) {
            if (this._filterIds.Count == 0 || type != XivChatType.SystemMessage) {
                return;
            }

            if (message.Payloads.Any(payload => payload is ItemPayload item && this._filterIds.Remove(item.ItemId))) {
                isHandled = true;
            }
        }

        private static void OnLogin(object? sender, EventArgs e) {
            _dresserContents = null;
        }

        private bool _wasEditing;

        private void OnFrameworkUpdate(Dalamud.Game.Framework framework) {
            var editing = Util.IsEditingPlate(this.Plugin.GameGui);
            if (!this._wasEditing && editing) {
                // cache dresser
                var unused = DresserContents;
            }

            this._wasEditing = editing;
        }

        internal unsafe bool ArmoireLoaded => *(byte*) this._armoirePtr > 0;

        internal string? ExamineName => this._examineNamePtr == IntPtr.Zero
            ? null
            : MemoryHelper.ReadStringNullTerminated(this._examineNamePtr);

        private static readonly Stopwatch DresserTimer = new();
        private static List<GlamourItem>? _dresserContents;

        internal static unsafe List<GlamourItem> DresserContents {
            get {
                if (_dresserContents != null && DresserTimer.Elapsed < TimeSpan.FromSeconds(1)) {
                    return _dresserContents;
                }

                var list = new List<GlamourItem>();

                var agents = Framework.Instance()->GetUiModule()->GetAgentModule();
                var dresserAgent = agents->GetAgentByInternalId(AgentId.MiragePrismPrismBox);

                var itemsStart = *(IntPtr*) ((IntPtr) dresserAgent + 0x28);
                if (itemsStart == IntPtr.Zero) {
                    return _dresserContents ?? list;
                }

                for (var i = 0; i < 800; i++) {
                    var glamItem = *(GlamourItem*) (itemsStart + i * 28);
                    if (glamItem.ItemId == 0) {
                        continue;
                    }

                    list.Add(glamItem);
                }

                _dresserContents = list;
                DresserTimer.Restart();

                return list;
            }
        }

        internal static unsafe Dictionary<PlateSlot, SavedGlamourItem>? CurrentPlate {
            get {
                var agent = EditorAgent;
                if (agent == null) {
                    return null;
                }

                var editorInfo = *(IntPtr*) ((IntPtr) agent + 0x28);
                if (editorInfo == IntPtr.Zero) {
                    return null;
                }

                var plate = new Dictionary<PlateSlot, SavedGlamourItem>();
                foreach (var slot in (PlateSlot[]) Enum.GetValues(typeof(PlateSlot))) {
                    // Updated: 6.1
                    // from SetGlamourPlateSlot
                    var item = editorInfo + 44 * (int) slot + 10596;

                    var itemId = *(uint*) item;
                    var stainId = *(byte*) (item + 24);
                    var stainPreviewId = *(byte*) (item + 25);
                    var actualStainId = stainPreviewId == 0 ? stainId : stainPreviewId;

                    if (itemId == 0) {
                        continue;
                    }

                    plate[slot] = new SavedGlamourItem {
                        ItemId = itemId,
                        StainId = actualStainId,
                    };
                }

                return plate;
            }
        }

        private static unsafe AgentInterface* EditorAgent => Framework.Instance()->GetUiModule()->GetAgentModule()->GetAgentByInternalId(AgentId.MiragePrismMiragePlate);

        internal unsafe void SetGlamourPlateSlot(MirageSource source, int glamId, uint itemId, byte stainId) {
            this._setGlamourPlateSlot((IntPtr) EditorAgent, source, glamId, itemId, stainId);
        }

        internal unsafe void ModifyGlamourPlateSlot(PlateSlot slot, byte stainId, IntPtr numbers, int stainItemId) {
            this._modifyGlamourPlateSlot((IntPtr) EditorAgent, slot, stainId, numbers, stainItemId);
        }

        internal bool IsInArmoire(uint itemId) {
            var row = this.Plugin.DataManager.GetExcelSheet<Cabinet>()!.FirstOrDefault(row => row.Item.Row == itemId);
            if (row == null) {
                return false;
            }

            return this._isInArmoire(this._armoirePtr, (int) row.RowId) != 0;
        }

        internal uint? ArmoireIndexIfPresent(uint itemId) {
            var row = this.Plugin.DataManager.GetExcelSheet<Cabinet>()!.FirstOrDefault(row => row.Item.Row == itemId);
            if (row == null) {
                return null;
            }

            var isInArmoire = this._isInArmoire(this._armoirePtr, (int) row.RowId) != 0;
            return isInArmoire
                ? row.RowId
                : null;
        }

        internal unsafe void LoadPlate(SavedPlate plate) {
            var agent = EditorAgent;
            if (agent == null) {
                return;
            }

            // Updated: 6.11 C98BC0
            var editorInfo = *(IntPtr*) ((IntPtr) agent + 0x28);
            if (editorInfo == IntPtr.Zero) {
                return;
            }

            var dresser = DresserContents;
            var current = CurrentPlate;
            var usedStains = new Dictionary<(uint, uint), uint>();

            // Updated: 6.11 C984CF
            // current plate 6.11 C9AC9F
            var slotPtr = (PlateSlot*) (editorInfo + 0x18);
            var initialSlot = *slotPtr;
            foreach (var (slot, item) in plate.Items) {
                if (current != null && current.TryGetValue(slot, out var currentItem)) {
                    if (currentItem.ItemId == item.ItemId && currentItem.StainId == item.StainId) {
                        // ignore already-correct items
                        continue;
                    }
                }

                *slotPtr = slot;
                if (item.ItemId == 0) {
                    this._clearGlamourPlateSlot((IntPtr) agent, slot);
                    continue;
                }

                var source = MirageSource.GlamourDresser;
                var info = (0, 0u, (byte) 0);
                // find an item in the dresser that matches
                var matchingIds = dresser.FindAll(mirage => mirage.ItemId % Util.HqItemOffset == item.ItemId);
                if (matchingIds.Count == 0) {
                    // if not in the glamour dresser, look in the armoire
                    if (this.ArmoireIndexIfPresent(item.ItemId) is { } armoireIdx) {
                        source = MirageSource.Armoire;
                        info = ((int) armoireIdx, item.ItemId, 0);
                    }
                } else {
                    // try to find an item with a matching stain
                    var idx = matchingIds.FindIndex(mirage => mirage.StainId == item.StainId);
                    if (idx == -1) {
                        idx = 0;
                    }

                    var mirage = matchingIds[idx];
                    info = ((int) mirage.Index, mirage.ItemId, mirage.StainId);
                }

                if (info.Item1 == 0) {
                    continue;
                }

                this._setGlamourPlateSlot(
                    (IntPtr) agent,
                    source,
                    info.Item1,
                    info.Item2,
                    info.Item3
                );

                if (item.StainId != info.Item3) {
                    // mirage in dresser did not have stain for this item, so apply it
                    this.ApplyStain(agent, slot, item, usedStains);
                }
            }

            // restore initial slot, since changing this does not update the ui
            *slotPtr = initialSlot;
        }

        private static readonly InventoryType[] PlayerInventories = {
            InventoryType.Inventory1,
            InventoryType.Inventory2,
            InventoryType.Inventory3,
            InventoryType.Inventory4,
        };

        private unsafe void ApplyStain(AgentInterface* editorAgent, PlateSlot slot, SavedGlamourItem item, Dictionary<(uint, uint), uint> usedStains) {
            // find the dye for this stain in the player's inventory
            var inventory = InventoryManager.Instance();
            var transient = this.Plugin.DataManager.GetExcelSheet<StainTransient>()!.GetRow(item.StainId);
            (int itemId, int qty, int inv, int slot) dyeInfo = (0, 0, -1, 0);
            var items = new[] { transient?.Item1?.Value, transient?.Item2?.Value };
            foreach (var dyeItem in items) {
                if (dyeItem == null || dyeItem.RowId == 0) {
                    continue;
                }

                if (dyeInfo.itemId == 0) {
                    // use the first one (free one) as placeholder
                    dyeInfo.itemId = (int) dyeItem.RowId;
                }

                foreach (var type in PlayerInventories) {
                    var inv = inventory->GetInventoryContainer(type);
                    if (inv == null) {
                        continue;
                    }

                    for (var i = 0; i < inv->Size; i++) {
                        var address = ((uint) type, (uint) i);
                        var invItem = inv->Items[i];
                        if (invItem.ItemID != dyeItem.RowId) {
                            continue;
                        }

                        if (usedStains.TryGetValue(address, out var numUsed) && numUsed >= invItem.Quantity) {
                            continue;
                        }

                        // first one that we find in the inventory is the one we'll use
                        dyeInfo = ((int) dyeItem.RowId, (int) inv->Items[i].Quantity, (int) type, i);
                        if (usedStains.ContainsKey(address)) {
                            usedStains[address] += 1;
                        } else {
                            usedStains[address] = 1;
                        }

                        goto NoBreakLabels;
                    }
                }

                NoBreakLabels:
                {
                }
            }

            // do nothing if there is no dye item found
            if (dyeInfo.itemId == 0) {
                return;
            }

            var info = new ColorantInfo((uint) dyeInfo.inv, (ushort) dyeInfo.slot, (uint) dyeInfo.itemId, (uint) dyeInfo.qty);

            // allocate 24 bytes to store dye info if we have the dye
            var mem = dyeInfo.inv == -1
                ? IntPtr.Zero
                : Marshal.AllocHGlobal(24);

            if (mem != IntPtr.Zero) {
                *(ColorantInfo*) mem = info;
            }

            this._modifyGlamourPlateSlot(
                (IntPtr) editorAgent,
                slot,
                item.StainId,
                mem,
                dyeInfo.Item1
            );

            if (mem != IntPtr.Zero) {
                Marshal.FreeHGlobal(mem);
            }
        }

        internal void TryOn(uint itemId, byte stainId, bool suppress = true) {
            if (suppress) {
                this._filterIds.Add(itemId);
            }

            this._tryOn(0xFF, itemId % Util.HqItemOffset, stainId, 0, 0);
        }
    }

    internal enum MirageSource {
        GlamourDresser = 1,
        Armoire = 2,
    }

    internal enum PlateSlot : uint {
        MainHand = 0,
        OffHand = 1,
        Head = 2,
        Body = 3,
        Hands = 4,
        Legs = 5,
        Feet = 6,
        Ears = 7,
        Neck = 8,
        Wrists = 9,
        RightRing = 10,
        LeftRing = 11,
    }

    internal static class PlateSlotExt {
        internal static string Name(this PlateSlot slot) {
            return slot switch {
                PlateSlot.MainHand => "Main Hand",
                PlateSlot.OffHand => "Off Hand",
                PlateSlot.Head => "Head",
                PlateSlot.Body => "Body",
                PlateSlot.Hands => "Hands",
                PlateSlot.Legs => "Legs",
                PlateSlot.Feet => "Feet",
                PlateSlot.Ears => "Ears",
                PlateSlot.Neck => "Neck",
                PlateSlot.Wrists => "Wrists",
                PlateSlot.RightRing => "Right Ring",
                PlateSlot.LeftRing => "Left Ring",
                _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
            };
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 28)]
    internal readonly struct GlamourItem {
        [FieldOffset(4)]
        internal readonly uint Index;

        [FieldOffset(8)]
        internal readonly uint ItemId;

        [FieldOffset(26)]
        internal readonly byte StainId;
    }

    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")]
    internal readonly struct ColorantInfo {
        private readonly uint InventoryId;
        private readonly ushort InventorySlot;
        private readonly byte Unk3;
        private readonly byte Unk4;
        private readonly uint StainItemId;
        private readonly uint StainItemCount;
        private readonly ulong Unk7;

        internal ColorantInfo(uint inventoryId, ushort inventorySlot, uint stainItemId, uint stainItemCount) {
            this.InventoryId = inventoryId;
            this.InventorySlot = inventorySlot;
            this.StainItemId = stainItemId;
            this.StainItemCount = stainItemCount;

            this.Unk3 = 0;
            this.Unk4 = 0;
            this.Unk7 = 0;
        }
    }
}
