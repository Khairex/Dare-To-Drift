using BepInEx;
using RoR2;
using System.Reflection;
using UnityEngine;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using R2API.AssetPlus;
using RoR2.WwiseUtils;
using System;
using System.Linq;
using System.IO;
using R2API.Utils;
using System.Collections.Generic;
using R2API;

namespace DareToDrift
{
    public class DriftItemDef
    {
        public static ItemDef InitializeItemDef()
        {
            ItemDef itemDef = new ItemDef
            {
                //More on these later
                nameToken = "DRIFTWHEELS_NAME",
                pickupToken = "DRIFTWHEELS_PICKUP",
                descriptionToken = "DRIFTWHEELS_DESC",
                loreToken = "DRIFTWHEELS_LORE",
                //The tier determines what rarity the item is: Tier1=white, Tier2=green, Tier3=red, Lunar=Lunar, Boss=yellow, and finally NoTier is generally used for helper items, like the tonic affliction
                tier = ItemTier.Lunar,
                //You can create your own icons and prefabs through assetbundles, but to keep this boilerplate brief, we'll be using question marks.
                pickupIconPath = "Textures/MiscIcons/texMysteryIcon",
                pickupModelPath = "Prefabs/PickupModels/PickupFossil",
                //Can remove determines if a shrine of order, or a printer can take this item, generally true, except for NoTier items.
                canRemove = true,
                //Hidden means that there will be no pickup notification, and it won't appear in the inventory at the top of the screen. This is useful for certain noTier helper items, such as the DrizzlePlayerHelper.
                hidden = false
            };

            //The Name should be self explanatory
            R2API.AssetPlus.Languages.AddToken(itemDef.nameToken, "Drift Wheels");
            //The Pickup is the short text that appears when you first pick this up. This text should be short and to the point, nuimbers are generally ommited.
            R2API.AssetPlus.Languages.AddToken(itemDef.pickupToken, "Drifting To The MAX");
            //The Description is where you put the actual numbers and give an advanced description.
            R2API.AssetPlus.Languages.AddToken(itemDef.descriptionToken, "Adds a <style=cIsUtility>10%</style> sprint speed bonus <style=cStack>(+10% per stack)</style>. Decreased Friction");
            //The Lore is, well, flavor. You can write pretty much whatever you want here.
            R2API.AssetPlus.Languages.AddToken(itemDef.loreToken, "If you close your eyes, you can hear Running in the 90s getting louder");

            //You can add your own display rules here, where the first argument passed are the default display rules: the ones used when no specific display rules for a character are found.
            //For this example, we are omitting them, as they are quite a pain to set up.
            var displayRules = new ItemDisplayRuleDict(null);

            //Then finally add it to R2API
            ItemAPI.Add(new CustomItem(itemDef, displayRules));

            return itemDef;
        }
    }
}
