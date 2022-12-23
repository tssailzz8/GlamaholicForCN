using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;

namespace Glamaholic {
    [Serializable]
    internal class Configuration : IPluginConfiguration {
        public int Version { get; set; } = 1;

        public List<SavedPlate> Plates { get; init; } = new();
        public bool ShowEditorMenu = true;
        public bool ShowExamineMenu = true;
        public bool ShowTryOnMenu = true;
        public bool ShowKofiButton = true;
        public bool ItemFilterShowObtainedOnly;

        internal static void SanitisePlate(SavedPlate plate) {
            var valid = Enum.GetValues<PlateSlot>();
            foreach (var slot in plate.Items.Keys.ToArray()) {
                if (!valid.Contains(slot)) {
                    plate.Items.Remove(slot);
                }
            }
        }

        internal void AddPlate(SavedPlate plate) {
            SanitisePlate(plate);
            this.Plates.Add(plate);
        }
    }

    [Serializable]
    internal class SavedPlate {
        public string Name { get; set; }
        public Dictionary<PlateSlot, SavedGlamourItem> Items { get; init; } = new();
        public List<string> Tags { get; } = new();

        public SavedPlate(string name) {
            this.Name = name;
        }

        internal SavedPlate Clone() {
            return new SavedPlate(this.Name) {
                Items = this.Items.ToDictionary(entry => entry.Key, entry => entry.Value.Clone()),
            };
        }
    }

    [Serializable]
    internal class SavedGlamourItem {
        public uint ItemId { get; set; }
        public byte StainId { get; set; }

        internal SavedGlamourItem Clone() {
            return new SavedGlamourItem() {
                ItemId = this.ItemId,
                StainId = this.StainId,
            };
        }
    }
}
