using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Glamaholic {
    [Serializable]
    internal class SharedPlate {
        public string Name { get; }
        public Dictionary<PlateSlot, SavedGlamourItem> Items { get; }

        internal SharedPlate(SavedPlate plate) {
            var clone = plate.Clone();
            this.Name = clone.Name;
            this.Items = clone.Items;
        }

        [JsonConstructor]
        private SharedPlate(string name, Dictionary<PlateSlot, SavedGlamourItem> items) {
            this.Name = name;
            this.Items = items;
        }

        internal SavedPlate ToPlate() {
            return new SavedPlate(this.Name) {
                Items = this.Items.ToDictionary(entry => entry.Key, entry => entry.Value.Clone()),
            };
        }
    }
}
