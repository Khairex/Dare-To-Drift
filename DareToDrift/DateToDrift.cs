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
//using Hj;

namespace DareToDrift
{
    [BepInDependency(R2API.R2API.PluginGUID, BepInDependency.DependencyFlags.HardDependency)]
    [R2APISubmoduleDependency(nameof(ItemAPI), nameof(ItemDropAPI), nameof(R2API.AssetPlus.AssetPlus))]
    //[BepInDependency(Hj.HjUpdaterAPI.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [R2APISubmoduleDependency(nameof(AssetPlus))]
    [BepInPlugin("com.khairex.daretodrift", MOD_NAME, "1.0.0")]
    public class DateToDrift : BaseUnityPlugin
    {
        public const string MOD_NAME = "DareToDrift";

        // soundbank events:

        // Used to start a single instance of music on a gameobject
        private const uint START_RUNNING = 3580630223;
        // Used to stop all music instances
        private const uint EVERYBODY_FREEZE = 3365085556;
        // Used to pause a single music instance on a gameobject
        private const uint BRIEF_RESPITE = 1249098644;
        // Used to resume a single music instance on a gameobject
        private const uint KEEP_GOING = 2606526925;

        private static List<SurvivorStatus> SurvivorsToTrack = new List<SurvivorStatus>();

        private int clientNumDriftItems = 0;
        private ItemDef driftItemDef = null;

        static uint LoadSoundBank(byte[] resourceBytes)
        {
            //Check to make sure that the byte array supplied is not null, and throw an appropriate exception if they are.
            if (resourceBytes == null) throw new ArgumentNullException(nameof(resourceBytes));

            //Register the soundbank and return the ID
            return R2API.SoundAPI.SoundBanks.Add(resourceBytes);
        }

        /// <summary>
        /// Static function to contain this HjUpdaterAPI line, because
        /// it doesn't work otherwise. Intuitive!
        /// </summary>
        private static void Updater()
        {
            //Hj.HjUpdaterAPI.Register(MOD_NAME);
        }

        //The Awake() method is run at the very start when the game is initialized.
        public void Awake()
        {
            driftItemDef = DriftItemDef.InitializeItemDef();

            // Optional auto-update functionality
            //if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(Hj.HjUpdaterAPI.GUID))
            //{
            //    Updater();
            //}

            //  Register the Running in the 90's sample, and the events that allow us to control when it plays
            uint unloadingID = LoadSoundBank(Properties.Resources.Nineties_Soundbank);

            On.RoR2.Stage.Start += Stage_Start;
            RoR2.Run.onRunStartGlobal += RunStart;

            // UNCOMMENT THIS FOR MULTIPLAYER TESTING USING SEVERAL LOCAL GAME INSTANCES
            On.RoR2.Networking.GameNetworkManager.OnClientConnect += GameNetworkManager_OnClientConnect1;
        }

        // UNCOMMENT THIS FOR MULTIPLAYER TESTING USING SEVERAL LOCAL GAME INSTANCES
        private void GameNetworkManager_OnClientConnect1(On.RoR2.Networking.GameNetworkManager.orig_OnClientConnect orig, RoR2.Networking.GameNetworkManager self, UnityEngine.Networking.NetworkConnection conn)
        {
            // Do nothing
        }

        private void RunStart(RoR2.Run run)
        {
            // Init Item Event
            CharacterMaster clientMaster = PlayerCharacterMasterController.instances[0].master;
            clientMaster.inventory.onInventoryChanged += () =>
            {
                int numDriftItems = clientMaster.inventory.GetItemCount(driftItemDef.itemIndex);
                int deltaItems = numDriftItems - clientNumDriftItems;

                clientMaster.playerCharacterMasterController.networkUser.GetCurrentBody().sprintingSpeedMultiplier += (deltaItems * .1f);
            };
        }

        // I noticed that going between stages (especially when looping back
        // to stages you've been to before, you'd hear orphan deja vu instances
        // floating in space. This performs orphancide.
        private void Stage_Start(On.RoR2.Stage.orig_Start orig, Stage self)
        {
            orig(self);

            AkSoundEngine.PostEvent(EVERYBODY_FREEZE, null);

            SurvivorsToTrack.Clear();
            foreach (var pcmc in PlayerCharacterMasterController.instances)
            {
                SurvivorsToTrack.Add(new SurvivorStatus(pcmc, false));
            }
        }

        private float driftPower = 0f;
        private float driftPowerBuildRate = 28f;
        private float driftPowerDecayRate = 8f;
        private const float driftPowerMax = 100f;
        private const int driftAttackSpeedBuff = 10;
        private int currentDriftBuffCount = 0;

        public void Update()
        {
            // TODO Remove test
            if (Input.GetKeyDown(KeyCode.F2))
            {
                PlayerCharacterMasterController.instances[0].master.inventory.GiveItem(ItemIndex.Hoof);
                PlayerCharacterMasterController.instances[0].master.inventory.GiveItem(ItemIndex.Hoof);
                PlayerCharacterMasterController.instances[0].master.inventory.GiveItem(ItemIndex.Hoof);
                PlayerCharacterMasterController.instances[0].master.inventory.GiveItem(ItemIndex.Hoof);
                PlayerCharacterMasterController.instances[0].master.inventory.GiveItem(ItemIndex.Hoof);
                PlayerCharacterMasterController.instances[0].master.inventory.GiveItem(ItemIndex.BoostAttackSpeed);
            }

            if (Input.GetKeyDown(KeyCode.F3))
            {
                //Get the player body to use a position:	
                var transform = PlayerCharacterMasterController.instances[0].master.GetBodyObject().transform;
                //And then drop our defined item in front of the player.
                PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(driftItemDef.itemIndex), transform.position, transform.forward * 20f);
            }

            foreach (var status in SurvivorsToTrack)
            {
                CharacterMotor motor = status.CharacterBody.characterMotor;

                const float downForceGrounded = 0.5f;
                const float downForceInAir = 0.1f;

                int driftItemCount = status.CharacterBody.inventory.GetItemCount(driftItemDef.itemIndex);

                float dotResult = 1f;

                if (driftItemCount > 0)
                {
                    const float frictionAmountMin = 0f;
                    const float frictionAmountMax = 20f;
                    const float frictionReductionTopSpeed = 200f;

                    float frictionReductionAmount = 0;
                    if (status.LastVelocity.magnitude > 0)
                    {
                        frictionReductionAmount = Mathf.Clamp01(status.LastVelocity.magnitude / frictionReductionTopSpeed);
                    }

                    // More friction while walking
                    if(!status.CharacterBody.isSprinting)
                    {
                        frictionReductionAmount *= 0.8f;
                    }

                    float friction = Mathf.Lerp(frictionAmountMax, frictionAmountMin, frictionReductionAmount);
                    Vector3 newVelocity = Vector3.Lerp(status.LastVelocity, motor.velocity, Time.deltaTime * friction);

                    float downForce = motor.isGrounded ? downForceGrounded : downForceInAir;
                    motor.velocity = new Vector3(newVelocity.x, motor.velocity.y - downForce, newVelocity.z);
                
                    CharacterDirection direction = status.CharacterBody.characterDirection;
                    dotResult = Vector2.Dot(new Vector2(status.LastVelocity.normalized.x , status.LastVelocity.normalized.z),
                                                  new Vector2(direction.forward.normalized.x , direction.forward.normalized.z));

                    const float driftThreshold = 0.95f;
                    const float driftSpeedThreshold = 5f;

                    if (dotResult <= driftThreshold && new Vector2(newVelocity.x, newVelocity.z).magnitude >= driftSpeedThreshold)
                        driftPower += Mathf.Clamp01((1 - dotResult) * 2) * driftPowerBuildRate * Time.deltaTime;

                }

                driftPower -= driftPowerDecayRate * Time.deltaTime;
                driftPower = Mathf.Clamp(driftPower, 0, driftPowerMax);

                int buffs = Mathf.FloorToInt((driftPower / driftPowerMax) * driftAttackSpeedBuff);

                if (currentDriftBuffCount != buffs)
                {
                    while (currentDriftBuffCount < buffs)
                    {
                        status.CharacterBody.AddBuff(BuffIndex.AttackSpeedOnCrit);
                        currentDriftBuffCount++;
                    }

                    while (currentDriftBuffCount > buffs)
                    {
                        status.CharacterBody.RemoveBuff(BuffIndex.AttackSpeedOnCrit);
                        currentDriftBuffCount--;
                    }

                    Debug.Log($"Current Drift buff level is {currentDriftBuffCount}");
                }

                // Update Music Volume Based On Current Velocity And Projected Velocity
                float passiveVolume = currentDriftBuffCount * currentDriftBuffCount;
                float activeVolume = dotResult <= 0.95f && motor.velocity.magnitude > 1f ? (1 - dotResult) * 50 : 0f;

                float targetVolume = passiveVolume * 0.85f + activeVolume * 0.15f;
                float newVolume = Mathf.Lerp(status.MusicVolume, targetVolume, Time.deltaTime * 15f);
                status.MusicVolume = newVolume;
                SetMusic(newVolume);

                status.LastVelocity = motor.velocity;
            }
        }

        // volume is 0-100
        public void SetMusic(float volume)
        {
            foreach (var status in SurvivorsToTrack)
            {
                if (volume <= 0 && status.MusicPlaying)
                {
                    // If it's NOT moving and the music IS playing, then PAUSE
                    status.MusicPlaying = false;
                    AkSoundEngine.PostEvent(BRIEF_RESPITE, status.ControllerGameObject);
                }
                else if (volume > 0 && !status.MusicPlaying)
                {
                    // If it IS moving and the music is NOT playing, then RESUME (or start, if it hasn't been started yet)
                    if (status.MusicStarted)
                    {
                        AkSoundEngine.PostEvent(KEEP_GOING, status.ControllerGameObject);
                    }
                    else
                    {
                        AkSoundEngine.PostEvent(START_RUNNING, status.ControllerGameObject);
                    }

                    status.MusicPlaying = true;
                }

                RtpcSetter gameParamSetter = new RtpcSetter("Speeds", status.ControllerGameObject) { value = volume };
                gameParamSetter.FlushIfChanged();
            }

        }

        public void FixedUpdate()
        {
            // To avoid trying to remove while iterating through our list
            List<SurvivorStatus> toRemove = new List<SurvivorStatus>();

            // I don't know 100% if this is needed, but I started getting
            // fewer errors with it in.
            int preRemoved = SurvivorsToTrack.RemoveAll((status) => status.Controller == null || !status.Controller.isConnected);

            foreach (var status in SurvivorsToTrack)
            {
                if (status.ControllerGameObject != null)
                {
                    float speed = status.Speed;

                    // Set the volume of the music to change depending on current speed
                    // Formula is this:
                    //          speedItems * .4 + speed/12 + sqrt(speedItems)*speed/22 - 1
                    //
                    // Explanation:
                    //      I wanted the following things to be true:
                    //          1. Nothing would be heard under normal circumstances with 0 speed items.
                    //          2. Volume would scale off of a combination of speedItems and speed
                    //          3. Volume would not fluctuate very noticeably as the turret slowed down/sped up.
                    //
                    //      After some tweaking, I ended up preferring a mix of additive and multiplicitave scaling.
                    //      The -1 at the end helps increase the number of speedItems you need before you hear anything.
                    
                    /*
                    float newVolumeModifier = status.NumSpeedItems * 0.4f + speed / 12 + ((float)Math.Sqrt(status.NumSpeedItems) * speed / 16) - 1;
                    RtpcSetter gameParamSetter = new RtpcSetter("Speeds", status.ControllerGameObject) { value = newVolumeModifier };
                    gameParamSetter.FlushIfChanged();

                    // Getting item counts from the inventory every update is probably less than optimal, but I'm too lazy to do better
                    float moveSpeedThreshold = MoveSpeedThreshold(status.Inventory.GetItemCount(ItemIndex.Hoof),
                        status.Inventory.GetItemCount(ItemIndex.SprintOutOfCombat), status.Controller.networkUser.GetCurrentBody().outOfCombat);

                    if (speed <= moveSpeedThreshold && status.MusicPlaying)
                    {
                        // If it's NOT moving and the music IS playing, then PAUSE
                        status.MusicPlaying = false;
                        AkSoundEngine.PostEvent(BRIEF_RESPITE, status.ControllerGameObject);
                    }
                    else if (speed > moveSpeedThreshold && !status.MusicPlaying)
                    {
                        // If it IS moving and the music is NOT playing, then RESUME (or start, if it hasn't been started yet)
                        if (status.MusicStarted)
                        {
                            AkSoundEngine.PostEvent(KEEP_GOING, status.ControllerGameObject);
                        }
                        else
                        {
                            AkSoundEngine.PostEvent(START_RUNNING, status.ControllerGameObject);
                        }

                        status.MusicPlaying = true;
                    }
                    */

                    // The last thing we want to do, prep for next update
                    status.RecordLastPosition();
                }
            }
        }

        public const float DEFAULT_BASE_SPEED = 7f;
        public const float HOOF_FACTOR = 0.14f;
        public const float WHIP_FACTOR = 0.3f;
        public const float PADDING = 1.15f;

        // Calculate a threshold value for speed
        // If moving slower than this, no music will be heard
        public static float MoveSpeedThreshold(int hooves, int whips, bool outOfCombat, float baseMoveSpeed = DEFAULT_BASE_SPEED)
        {
            float modifier = 1 + HOOF_FACTOR * hooves;
            if (outOfCombat)
            {
                modifier += WHIP_FACTOR * whips;
            }

            return baseMoveSpeed * modifier * PADDING;
        }
    }

    /// <summary>
    /// Just a convenient way of storing some basic info
    /// about a survivor that the plugin cares about
    /// </summary>
    public class SurvivorStatus
    {
        public PlayerCharacterMasterController Controller { get; set; }

        public GameObject ControllerGameObject
        {
            get
            {
                if (Controller is null)
                {
                    return null;
                }

                if (Controller.networkUser is null)
                {
                    return null;
                }

                if (Controller.networkUser.GetCurrentBody() is null)
                {
                    return null;
                }

                return Controller.networkUser.GetCurrentBody().gameObject;
            }
        }

        public CharacterBody CharacterBody
        {
            get
            {
                if (Controller is null)
                {
                    return null;
                }

                if (Controller.networkUser is null)
                {
                    return null;
                }

                return Controller.networkUser.GetCurrentBody();
            }
        }

        public Inventory Inventory
        {
            get
            {
                if (CharacterBody is null)
                {
                    return null;
                }

                return CharacterBody.inventory;
            }
        }

        public int NumSpeedItems
        {
            get
            {
                if (Inventory is null)
                {
                    return 0;
                }

                return Inventory.GetItemCount(ItemIndex.Hoof);
            }
        }

        private bool mMusicPlaying = false;
        public bool MusicPlaying
        {
            get => mMusicPlaying;
            set
            {
                mMusicPlaying = value;
                if (value)
                {
                    MusicStarted = true;
                }
            }
        }

        public bool MusicStarted { get; private set; } = false;

        // 0 - 100;
        public float MusicVolume = 0f;

        private Vector3 LastPosition { get; set; }
        public Vector3 LastVelocity { get; set; }

        public float Speed
        {
            get
            {
                if (LastPosition != null && ControllerGameObject != null)
                {
                    Vector3 diff = ControllerGameObject.transform.position - LastPosition;
                    diff = new Vector3(diff.x, diff.y / 3, diff.z);
                    return diff.magnitude / Time.fixedDeltaTime;
                }

                return 0;
            }
        }

        public SurvivorStatus(PlayerCharacterMasterController controller, bool musicPlaying)
        {
            Controller = controller;
            MusicPlaying = musicPlaying;
        }

        public void RecordLastPosition()
        {
            if (Controller != null)
            {
                LastPosition = ControllerGameObject.transform.position;
            }
        }
    }

}
