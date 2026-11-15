using System;
using System.Collections.Generic;
using System.Linq;
using BoneLib;
using BoneLib.BoneMenu;
using Il2CppSLZ.Marrow.Warehouse;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(ItemShooter.Main), "ItemShooter", "1.0.0", "You")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace ItemShooter
{
    public class Main : MelonMod
    {
        // ---- settings (all editable from BoneMenu) ----
        private bool _enabled = true;
        private float _speed = 25f;       // launch speed in m/s
        private float _spread = 0f;       // random cone angle in degrees
        private float _cooldown = 0.25f;  // seconds between shots (hold button = auto fire)
        private float _lifetime = 15f;    // seconds before a shot item is despawned (0 = never)

        // ---- selected item ----
        private string _selectedBarcode = null;
        private string _selectedTitle = "None";
        private FunctionElement _selectedLabel;

        private Page _itemsPage;
        private bool _itemsBuilt = false;
        private float _nextShotTime = 0f;

        public override void OnInitializeMelon()
        {
            BuildMenu();
        }

        public override void OnLevelInitialized(LevelInfo info)
        {
            // The warehouse is ready once a level is loaded, so build the item list then (only once).
            if (!_itemsBuilt)
                BuildItemList();
        }

        // ------------------------------------------------------------------
        // BoneMenu
        // ------------------------------------------------------------------
        private void BuildMenu()
        {
            Page root = Page.Root.CreatePage("Item Shooter", Color.cyan);

            root.CreateBool("Enabled", Color.white, _enabled, v => _enabled = v);
            _selectedLabel = root.CreateFunction("Selected: None", Color.yellow, () => { });
            _itemsPage = root.CreatePage("Pick Item", Color.green);
            root.CreateFloat("Speed", Color.white, 5f, _speed, 1f, 200f, v => _speed = v);
            root.CreateFloat("Spread", Color.white, 1f, _spread, 0f, 45f, v => _spread = v);
            root.CreateFloat("Fire Delay", Color.white, 0.05f, _cooldown, 0.05f, 2f, v => _cooldown = v);
            root.CreateFloat("Despawn After (s)", Color.white, 1f, _lifetime, 0f, 120f, v => _lifetime = v);
            root.CreateFunction("Shoot Now", Color.red, Shoot);

            _itemsPage.CreateFunction("(open a level to load items)", Color.gray, () => { });
        }

        private void BuildItemList()
        {
            var warehouse = AssetWarehouse.Instance;
            if (warehouse == null) return;

            // Collect every spawnable (vanilla + any mods you have installed).
            var items = new List<(string title, string barcode)>();
            foreach (var crate in warehouse.GetCrates())
            {
                var spawnable = crate.TryCast<SpawnableCrate>();
                if (spawnable == null) continue;
                string title = spawnable.Title;
                if (string.IsNullOrWhiteSpace(title)) continue;
                items.Add((title, spawnable.Barcode.ID));
            }
            if (items.Count == 0) return;

            items = items.OrderBy(i => i.title, StringComparer.OrdinalIgnoreCase).ToList();

            _itemsPage.RemoveAll();

            // Group A-Z so the menu stays usable: "A", "B", ... "#"
            foreach (var group in items.GroupBy(i => GroupKey(i.title)).OrderBy(g => g.Key))
            {
                Page letterPage = _itemsPage.CreatePage(group.Key, Color.white);
                foreach (var item in group)
                {
                    string title = item.title;
                    string barcode = item.barcode;
                    letterPage.CreateFunction(title, Color.white, () => SelectItem(title, barcode));
                }
            }

            _itemsBuilt = true;
            MelonLogger.Msg($"Item Shooter: loaded {items.Count} items into BoneMenu.");
        }

        private static string GroupKey(string title)
        {
            char c = char.ToUpperInvariant(title.TrimStart()[0]);
            return char.IsLetter(c) ? c.ToString() : "#";
        }

        private void SelectItem(string title, string barcode)
        {
            _selectedTitle = title;
            _selectedBarcode = barcode;
            if (_selectedLabel != null)
                _selectedLabel.ElementName = $"Selected: {title}";
            Notifier.Send(new Notification
            {
                Title = "Item Shooter",
                Message = $"Selected {title}",
                PopupLength = 1.5f,
                ShowTitleOnPopup = true
            });
        }

        // ------------------------------------------------------------------
        // Input + shooting
        // ------------------------------------------------------------------
        public override void OnUpdate()
        {
            if (!_enabled || _selectedBarcode == null) return;
            if (Time.time < _nextShotTime) return;

            // Right-hand A button fires. Hold it for auto-fire.
            if (Player.RightController != null && Player.RightController.GetAButton())
                Shoot();
        }

        private void Shoot()
        {
            if (_selectedBarcode == null) return;
            if (Player.RightHand == null || Player.Head == null) return;

            _nextShotTime = Time.time + _cooldown;

            Transform hand = Player.RightHand.transform;
            Vector3 dir = Player.Head.forward;
            if (_spread > 0f)
                dir = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), Player.Head.forward)
                    * Quaternion.AngleAxis(UnityEngine.Random.Range(0f, _spread), Player.Head.right)
                    * dir;

            Vector3 spawnPos = hand.position + dir * 0.25f;
            Quaternion spawnRot = Quaternion.LookRotation(dir);
            Vector3 velocity = dir * _speed;
            float life = _lifetime;

            HelperMethods.SpawnCrate(_selectedBarcode, spawnPos, spawnRot, Vector3.one, false, go =>
            {
                if (go == null) return;

                // Launch every rigidbody so multi-part items fly together.
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>())
                    rb.velocity = velocity;

                if (life > 0f)
                    UnityEngine.Object.Destroy(go, life);
            });
        }
    }
}
