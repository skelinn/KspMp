# KSP 1.12.5 API notes (generated from the decompiled Assembly-CSharp on 2026-09-02)

Verification of the API names the plan relies on. Regenerate with `scripts/decompile.sh` + this list.

## KSPAssemblyDependency ctor
- `KSPAssemblyDependency.cs:14` public KSPAssemblyDependency(string name, int versionMajor, int versionMinor)
- `KSPAssemblyDependency.cs:22` public KSPAssemblyDependency(string name, int versionMajor, int versionMinor, int versionRevision)

## CrewTransfer.CrewTransferData.canTransfer
- `CrewTransfer.cs:9` public bool canTransfer = true;
- `CrewTransfer.cs:120` crewTransferData.canTransfer = true;
- `CrewTransfer.cs:126` if (!crewTransferData.canTransfer)

## ControlTypes VESSEL_SWITCHING / EVA_INPUT / ALL_SHIP_CONTROLS
- `ControlTypes.cs:24` VESSEL_SWITCHING = 0x10000uL,
- `ControlTypes.cs:36` EVA_INPUT = 0x10000000uL,
- `ControlTypes.cs:75` MAPVIEW = ~(UI | MAP | PAUSE | MISC | CAMERACONTROLS | TIMEWARP | QUICKSAVE | QUICKLOAD | VESSEL_SWITCHING | EDITOR_EDIT_NAME_FIELDS | GUI | TARGETING | MANNODE_ADDEDIT |
- `ControlTypes.cs:79` ALL_SHIP_CONTROLS = ~(UI | MAP | PAUSE | CAMERAMODES | CAMERACONTROLS | TIMEWARP | QUICKSAVE | QUICKLOAD | VESSEL_SWITCHING | GUI | TARGETING | MANNODE_ADDEDIT | MANNODE_
- `ControlTypes.cs:80` ALL_SHIP_CONTROLS_ALLOW_UIMODE = ~(UI | MAP | PAUSE | CAMERAMODES | CAMERACONTROLS | TIMEWARP | QUICKSAVE | QUICKLOAD | VESSEL_SWITCHING | GUI | FLIGHTUIMODE | TARGETING 

## Vessel.GetReferenceTransformPart / CrewListSetDirty / RebuildCrewList / OnFlyByWire
- `Vessel.cs:415` public FlightInputCallback OnFlyByWire;
- `Vessel.cs:1367` OnFlyByWire = flightInputCallback4;
- `Vessel.cs:1414` public Part GetReferenceTransformPart()
- `Vessel.cs:2470` RebuildCrewList();
- `Vessel.cs:2479` public void CrewListSetDirty()
- `Vessel.cs:2484` public void RebuildCrewList()
- `Vessel.cs:2752` vessel.RebuildCrewList();
- `Vessel.cs:2766` RebuildCrewList();
- `Vessel.cs:3422` RebuildCrewList();
- `Vessel.cs:3821` RebuildCrewList();
- `Vessel.cs:4029` RebuildCrewList();
- `Vessel.cs:4398` public ProtoVessel BackupVessel()

## BaseField.OnValueModified
- `BaseField.cs:43` private Callback<object> m_OnValueModified;
- `BaseField.cs:141` public event Callback<object> OnValueModified
- `BaseField.cs:146` Callback<object> callback = this.OnValueModified;
- `BaseField.cs:152` callback = Interlocked.CompareExchange(ref this.OnValueModified, value2, callback2);
- `BaseField.cs:172` Callback<object> callback = this.OnValueModified;
- `BaseField.cs:178` callback = Interlocked.CompareExchange(ref this.OnValueModified, value2, callback2);
- `BaseField.cs:219` this.OnValueModified = callback;
- `BaseField.cs:245` this.OnValueModified = callback;
- `BaseField.cs:304` this.OnValueModified(newValue);

## UI_Control.onFieldChanged
- `UI_Control.cs:19` public Callback<BaseField, object> onFieldChanged;

## EditorLogic attachPart/detachPart/SpawnPart/DeletePart/launchVessel/SetBackup/RestoreState
- `EditorLogic.cs:7535` public void SetBackup()
- `EditorLogic.cs:7766` private void RestoreState(int offset)
- `EditorLogic.cs:8233` public void SpawnPart(AvailablePart partInfo)
- `EditorLogic.cs:8626` public static void DeletePart(Part part)
- `EditorLogic.cs:10119` private void deleteSymmetryParts()
- `EditorLogic.cs:11370` private void attachPart(Part part, Attachment attach)
- `EditorLogic.cs:11477` private void detachPart(Part part)
- `EditorLogic.cs:14324` public void launchVessel()
- `EditorLogic.cs:14329` public void launchVessel(string siteName)

## ShipConstruct SaveShip/LoadShip/Clear/Add
- `ShipConstruct.cs:179` public ConfigNode SaveShip()
- `ShipConstruct.cs:488` public bool LoadShip(ConfigNode root, uint persistentID)
- `ShipConstruct.cs:2701` public bool LoadShip(ConfigNode root)
- `ShipConstruct.cs:2799` public void Clear()
- `ShipConstruct.cs:2804` public void Add(Part p)

## ConstructionEventType members
- `ConstructionEventType.cs:3` Unknown,
- `ConstructionEventType.cs:4` PartCreated,
- `ConstructionEventType.cs:5` PartDropped,
- `ConstructionEventType.cs:6` PartPicked,
- `ConstructionEventType.cs:7` PartDragging,
- `ConstructionEventType.cs:8` PartAttached,
- `ConstructionEventType.cs:9` PartDetached,
- `ConstructionEventType.cs:10` PartDeleted,
- `ConstructionEventType.cs:11` PartCopied,
- `ConstructionEventType.cs:12` PartRootSelected,
- `ConstructionEventType.cs:13` PartOffsetting,
- `ConstructionEventType.cs:14` PartOffset,
- `ConstructionEventType.cs:15` PartRotating,
- `ConstructionEventType.cs:16` PartRotated,
- `ConstructionEventType.cs:17` PartTweaked,
- `ConstructionEventType.cs:18` PartSymmetryDeleted,
- `ConstructionEventType.cs:19` PartOverInventoryGrid,
- `ConstructionEventType.cs:20` PartPickedInInventoryGrid,
- `ConstructionEventType.cs:21` PartDroppedInInventoryGrid

## ProtoCrewMember StartRespawnPeriod / SetTimeForRespawn / seatIdx / Die
- `ProtoCrewMember.cs:87` public double inactiveTimeEnd;
- `ProtoCrewMember.cs:127` public int seatIdx = -1;
- `ProtoCrewMember.cs:643` inactiveTimeEnd = copyOf.inactiveTimeEnd;
- `ProtoCrewMember.cs:3332` public void Die()
- `ProtoCrewMember.cs:3422` public void StartRespawnPeriod(double timeToRespawn = -1.0)
- `ProtoCrewMember.cs:3444` public void SetTimeForRespawn(double UTforRespawn)

## GameParameters RespawnTimer
- `GameParameters.cs:209` public float RespawnTimer = (float)GameSettings.DEFAULT_KERBAL_RESPAWN_TIMER;

## TimingManager.TimingStage
- `TimingManager.cs:6` public enum TimingStage
- `TimingManager.cs:10` Precalc,
- `TimingManager.cs:343` case TimingStage.Precalc:
- `TimingManager.cs:504` case TimingStage.Precalc:
- `TimingManager.cs:541` public static void FixedUpdateAdd(TimingStage stage, UpdateAction action)
- `TimingManager.cs:615` case TimingStage.Precalc:
- `TimingManager.cs:776` case TimingStage.Precalc:
- `TimingManager.cs:887` case TimingStage.Precalc:
- `TimingManager.cs:1048` case TimingStage.Precalc:

## MainMenu.Start
- `MainMenu.cs:239` private void Start()

## GameEvents (crew/kerbal/vessel/editor/docking)
- `GameEvents.cs:238` public static EventData<CrewTransfer.CrewTransferData> onCrewTransferSelected = new EventData<CrewTransfer.CrewTransferData>("onCrewTransferSelected");
- `GameEvents.cs:246` public static EventData<ProtoCrewMember, Part, Transform> onAttemptEva = new EventData<ProtoCrewMember, Part, Transform>("onAttemptEva");
- `GameEvents.cs:248` public static EventData<EventReport> onCrewKilled = new EventData<EventReport>("onCrewKilled");
- `GameEvents.cs:250` public static EventData<ProtoCrewMember> onKerbalAdded = new EventData<ProtoCrewMember>("onKerbalCreated");
- `GameEvents.cs:254` public static EventData<ProtoCrewMember> onKerbalRemoved = new EventData<ProtoCrewMember>("onKerbalRemoved");
- `GameEvents.cs:258` public static EventData<ProtoCrewMember, string, string> onKerbalNameChanged = new EventData<ProtoCrewMember, string, string>("onKerbalNameChanged");
- `GameEvents.cs:260` public static EventData<ProtoCrewMember, ProtoCrewMember.KerbalType, ProtoCrewMember.KerbalType> onKerbalTypeChange = new EventData<ProtoCrewMember, ProtoCrewMember.Kerba
- `GameEvents.cs:262` public static EventData<ProtoCrewMember, ProtoCrewMember.RosterStatus, ProtoCrewMember.RosterStatus> onKerbalStatusChange = new EventData<ProtoCrewMember, ProtoCrewMember
- `GameEvents.cs:270` public static EventData<ProtoCrewMember> onKerbalLevelUp = new EventData<ProtoCrewMember>("onKerbalLevelUp");
- `GameEvents.cs:300` public static EventData<Vessel> onVesselCreate = new EventData<Vessel>("onVesselCreate");
- `GameEvents.cs:302` public static EventData<Vessel> onVesselDestroy = new EventData<Vessel>("onVesselDestroy");
- `GameEvents.cs:306` public static EventData<Vessel> onVesselChange = new EventData<Vessel>("onVesselChange");
- `GameEvents.cs:308` public static EventData<Vessel, Vessel> onVesselSwitching = new EventData<Vessel, Vessel>("onVesselSwitching");
- `GameEvents.cs:322` public static EventData<Vessel> onVesselGoOnRails = new EventData<Vessel>("onVesselGoOnRails");
- `GameEvents.cs:324` public static EventData<Vessel> onVesselGoOffRails = new EventData<Vessel>("onVesselGoOffRails");
- `GameEvents.cs:330` public static EventData<Vessel> onVesselLoaded = new EventData<Vessel>("onVesselLoaded");
- `GameEvents.cs:336` public static EventData<Vessel> onVesselWasModified = new EventData<Vessel>("onVesselWasModified");
- `GameEvents.cs:340` public static EventData<Vessel> onVesselCrewWasModified = new EventData<Vessel>("onVesselCrewWasModified");
- `GameEvents.cs:352` public static EventData<ProtoVessel, bool> onVesselRecovered = new EventData<ProtoVessel, bool>("onVesselRecovered");
- `GameEvents.cs:404` public static EventData<Vessel, Vessel> onVesselsUndocking = new EventData<Vessel, Vessel>("onVesselsUndocking");
- `GameEvents.cs:410` public static EventData<int> onStageActivate = new EventData<int>("onStageActivate");
- `GameEvents.cs:430` public static EventData<Vector3d, Vector3d> onFloatingOriginShift = new EventData<Vector3d, Vector3d>("onFloatingOriginShift");
- `GameEvents.cs:442` public static EventData<GameScenes> onGameSceneLoadRequested = new EventData<GameScenes>("onGameSceneLoadRequested");
- `GameEvents.cs:448` public static EventData<GameScenes> onLevelWasLoadedGUIReady = new EventData<GameScenes>("onLevelWasLoadedGUIReady");
- `GameEvents.cs:530` public static EventData<Part> onPartUndock = new EventData<Part>("onPartUndock");
- `GameEvents.cs:761` public static EventData<ShipConstruct> onEditorShipModified = new EventData<ShipConstruct>("onEditorShipModified");
- `GameEvents.cs:771` public static EventData<int> onEditorSymmetryModeChange = new EventData<int>("onEditorSymmetryModeChange");
- `GameEvents.cs:777` public static EventData<ConstructionEventType, Part> onEditorPartEvent = new EventData<ConstructionEventType, Part>("onEditorPartEvent");
- `GameEvents.cs:817` public static EventData<Part, PartVariant> onEditorVariantApplied = new EventData<Part, PartVariant>("onEditorVariantApplied");

## FlightEVA.spawnEVA
- `FlightEVA.cs:92` public void spawnEVA()
- `FlightEVA.cs:334` public KerbalEVA spawnEVA(ProtoCrewMember pCrew, Part fromPart, Transform fromAirlock, bool tryAllHatches = false)

## KerbalEVA proceedAndBoard/BoardSeat/BoardPart
- `KerbalEVA.cs:17897` public virtual void BoardPart(Part p)
- `KerbalEVA.cs:18169` protected virtual void proceedAndBoard(Part p)
- `KerbalEVA.cs:18334` public virtual bool BoardSeat(KerbalSeat seat)

## Part Couple/Undock/decouple/craftID/flightID/AddCrewmember
- `Part.cs:178` public uint craftID;
- `Part.cs:180` public uint flightID;
- `Part.cs:4411` public void RemoveCrewmember(ProtoCrewMember crew)
- `Part.cs:4451` public bool AddCrewmember(ProtoCrewMember crew)
- `Part.cs:4536` public bool AddCrewmemberAt(ProtoCrewMember crew, int seatIndex)
- `Part.cs:5852` public void decouple(float breakForce = 0f)
- `Part.cs:6886` public void Couple(Part tgtPart)
- `Part.cs:7124` public void Undock(DockedVesselInfo newVesselInfo)

## ModuleDockingNode DockToVessel/Undock
- `ModuleDockingNode.cs:4440` public void DockToVessel(ModuleDockingNode node)
- `ModuleDockingNode.cs:4587` public void Undock()

## TimeWarp SetRate/GetMaxRateForAltitude/Mode
- `TimeWarp.cs:45` public Modes Mode;
- `TimeWarp.cs:1204` public static void SetRate(int rate_index, bool instant, bool postScreenMessage = true)
- `TimeWarp.cs:1913` public int GetMaxRateForAltitude(double altitude, CelestialBody cb)

## FlightDriver SetPause/StartAndFocusVessel/RevertToLaunch/ReturnToEditor
- `FlightDriver.cs:971` public static void SetPause(bool pauseState, bool postScreenMessage = true)
- `FlightDriver.cs:1107` public static void RevertToLaunch()
- `FlightDriver.cs:1121` public static void RevertToPrelaunch(EditorFacility facility)
- `FlightDriver.cs:1132` public static void ReturnToEditor(EditorFacility facility)
- `FlightDriver.cs:1141` public static void StartAndFocusVessel(string stateFileToLoad, int vesselToFocusIdx)
- `FlightDriver.cs:1149` public static void StartAndFocusVessel(Game stateToLoad, int vesselToFocusIdx)

## QuickSaveLoad.quickLoad
- `QuickSaveLoad.cs:402` quickLoad("quicksave", HighLogic.SaveFolder);
- `QuickSaveLoad.cs:490` quickLoad("quicksave", HighLogic.SaveFolder);
- `QuickSaveLoad.cs:681` private void quickLoad(string filename, string folder)
- `QuickSaveLoad.cs:1198` quickLoad(Path.GetFileNameWithoutExtension(saveName), HighLogic.SaveFolder);

## PauseMenu.drawStockRevertOptions
- `PauseMenu.cs:1334` drawStockRevertOptions(dialog, list);
- `PauseMenu.cs:1345` internal static void drawStockRevertOptions(PopupDialog dialog, List<DialogGUIBase> options)

## SpaceTracking.FlyVessel
- `KSP/UI/Screens/SpaceTracking.cs:994` FlyVessel(selectedVessel);
- `KSP/UI/Screens/SpaceTracking.cs:1415` private void FlyVessel(Vessel v)
- `KSP/UI/Screens/SpaceTracking.cs:1508` FlyVessel(selectedVessel);
- `KSP/UI/Screens/SpaceTracking.cs:1631` FlyVessel(selectedVessel);

## ShipConstruction.AssembleForLaunch
- `ShipConstruction.cs:830` public static void AssembleForLaunch(ShipConstruct ship, string landedAt, string displaylandedAt, string flagURL, Game sceneState, VesselCrewManifest crewManifest)

## OrbitPhysicsManager.HoldVesselUnpack
- `OrbitPhysicsManager.cs:161` public static void HoldVesselUnpack(int releaseAfter = 1)

## FlightGlobals SetActiveVessel/ForceSetActiveVessel/FindVessel/FindPartByID
- `FlightGlobals.cs:671` public static Vessel FindVessel(Guid id)
- `FlightGlobals.cs:719` public static bool FindVessel(uint id, out Vessel vessel)
- `FlightGlobals.cs:2495` public static bool SetActiveVessel(Vessel v)
- `FlightGlobals.cs:2517` public static bool ForceSetActiveVessel(Vessel v)
- `FlightGlobals.cs:4128` public static Part FindPartByID(uint flightID)

## KerbalRoster GetNewKerbal/SetExperienceTrait/SackAvailable/HireApplicant/AddCrewMember/Remove
- `KerbalRoster.cs:772` public ProtoCrewMember GetNewKerbal(ProtoCrewMember.KerbalType type = ProtoCrewMember.KerbalType.Crew)
- `KerbalRoster.cs:865` public bool AddCrewMember(ProtoCrewMember crew)
- `KerbalRoster.cs:965` public bool Exists(string name)
- `KerbalRoster.cs:970` public bool Remove(string name)
- `KerbalRoster.cs:995` public bool Remove(ProtoCrewMember crew)
- `KerbalRoster.cs:1000` public void Remove(int i)
- `KerbalRoster.cs:2690` public void HireApplicant(ProtoCrewMember ap)
- `KerbalRoster.cs:2726` public void SackAvailable(ProtoCrewMember ap)
- `KerbalRoster.cs:3087` public static void SetExperienceTrait(ProtoCrewMember pcm, string traitName = null)

## OrbitDriver.updateFromParameters
- `OrbitDriver.cs:147` updateFromParameters();
- `OrbitDriver.cs:332` updateFromParameters();
- `OrbitDriver.cs:602` public void updateFromParameters()
- `OrbitDriver.cs:604` updateFromParameters(setPosition: true);
- `OrbitDriver.cs:607` internal void updateFromParameters(bool setPosition)

## ConfigNode WriteNode/PreFormatConfig/RecurseFormat
- `ConfigNode.cs:8013` private static void RecurseFormat(List<string[]> cfg, ref int index, ConfigNode node)
- `ConfigNode.cs:8194` private static List<string[]> PreFormatConfig(string[] cfgData)
- `ConfigNode.cs:8539` private void WriteNode(StreamWriter sw)

## Planetarium.SetUniversalTime
- `Planetarium.cs:542` public static void SetUniversalTime(double t)

## Versioning.GetVersionString
- NO MATCH in Versioning.cs for `GetVersionString\(`

## InputLockManager.SetControlLock
- `InputLockManager.cs:37` public static ControlTypes SetControlLock(ControlTypes locks, string lockID)
- `InputLockManager.cs:76` public static ControlTypes SetControlLock(string lockID)

## StageManager ActivateNextStage/ActivateStage
- `KSP/UI/Screens/StageManager.cs:3203` public static void ActivateNextStage()
- `KSP/UI/Screens/StageManager.cs:3226` public static void ActivateStage(int stage)

## ActionGroupList ToggleGroup/SetGroup
- `ActionGroupList.cs:83` public void ToggleGroup(KSPActionGroup group)
- `ActionGroupList.cs:212` public void SetGroup(KSPActionGroup group, bool active)

## UIPartActionButton.OnClick
- `UIPartActionButton.cs:51` public void OnClick()

## VesselAutopilot SetMode/Enable/Disable
- `VesselAutopilot.cs:2438` public bool Enable()
- `VesselAutopilot.cs:2443` public bool Enable(AutopilotMode mode)
- `VesselAutopilot.cs:2481` public bool Disable()
- `VesselAutopilot.cs:2499` public bool SetMode(AutopilotMode mode)

## ProtoVessel Load/Save/ctor
- `ProtoVessel.cs:153` public ProtoVessel(Vessel VesselRef)
- `ProtoVessel.cs:158` public ProtoVessel(Vessel VesselRef, bool preCreate)
- `ProtoVessel.cs:326` public ProtoVessel(ConfigNode node, Game st)
- `ProtoVessel.cs:2199` public void Save(ConfigNode node)
- `ProtoVessel.cs:2440` public void Load(FlightState st)

## Game ctor / Start / Updated / SaveGame
- `Game.cs:216` public Game()
- `Game.cs:260` public Game(ConfigNode root)
- `Game.cs:1752` public void Start()

## GamePersistence.SaveGame
- `GamePersistence.cs:33` public static string SaveGame(string saveFileName, string saveFolder, SaveMode saveMode)
- `GamePersistence.cs:38` public static string SaveGame(Game game, string saveFileName, string saveFolder, SaveMode saveMode)
- `GamePersistence.cs:256` public static string SaveGame(GameBackup game, string saveFileName, string saveFolder, SaveMode saveMode)


# M2 vessel replication API (generated 2026-09-02)

## ProtoVessel ctor/Load/Save/vesselRef/vesselID
- `ProtoVessel.cs:9` public List<ProtoPartSnapshot> protoPartSnapshots;
- `ProtoVessel.cs:15` public OrbitSnapshot orbitSnapShot;
- `ProtoVessel.cs:17` public Guid vesselID = Guid.Empty;
- `ProtoVessel.cs:19` public uint persistentId;
- `ProtoVessel.cs:29` public bool landed;
- `ProtoVessel.cs:35` public bool splashed;
- `ProtoVessel.cs:49` public double altitude;
- `ProtoVessel.cs:51` public double latitude;
- `ProtoVessel.cs:53` public double longitude;
- `ProtoVessel.cs:59` public Quaternion rotation;
- `ProtoVessel.cs:63` public Vessel vesselRef;
- `ProtoVessel.cs:65` public Vessel.Situations situation;
- `ProtoVessel.cs:153` public ProtoVessel(Vessel VesselRef)
- `ProtoVessel.cs:158` public ProtoVessel(Vessel VesselRef, bool preCreate)
- `ProtoVessel.cs:326` public ProtoVessel(ConfigNode node, Game st)
- `ProtoVessel.cs:2199` public void Save(ConfigNode node)
- `ProtoVessel.cs:2440` public void Load(FlightState st)

## Vessel: BackupVessel/SetPosition/SetWorldVelocity/SetRotation/GoOnRails/GoOffRails/Load/Unload/state fields
- `Vessel.cs:98` public Guid id;
- `Vessel.cs:100` public uint persistentId;
- `Vessel.cs:102` public string vesselName;
- `Vessel.cs:106` public Part rootPart;
- `Vessel.cs:120` public OrbitDriver orbitDriver;
- `Vessel.cs:144` public Quaternion srfRelRotation;
- `Vessel.cs:146` public double longitude;
- `Vessel.cs:148` public double latitude;
- `Vessel.cs:150` public double altitude;
- `Vessel.cs:156` public bool loaded;
- `Vessel.cs:168` public bool packed;
- `Vessel.cs:170` public bool Landed;
- `Vessel.cs:172` public bool Splashed;
- `Vessel.cs:221` public Situations situation;
- `Vessel.cs:227` public Vector3d obt_velocity;
- `Vessel.cs:229` public Vector3d srf_velocity;
- `Vessel.cs:265` public Vector3d CoMD;
- `Vessel.cs:267` public Vector3 angularVelocity;
- `Vessel.cs:347` public VesselType vesselType;
- `Vessel.cs:349` public KerbalEVA evaController;
- `Vessel.cs:421` public VesselRanges vesselRanges;
- `Vessel.cs:451` public Transform vesselTransform;
- `Vessel.cs:675` public Orbit orbit => orbitDriver.orbit;
- `Vessel.cs:677` public CelestialBody mainBody => orbit.referenceBody;
- `Vessel.cs:681` public bool LandedOrSplashed
- `Vessel.cs:708` public bool LandedInKSC
- `Vessel.cs:764` public bool LandedInStockLaunchSite
- `Vessel.cs:1044` public bool isEVA => vesselType == VesselType.EVA;
- `Vessel.cs:1587` public Vector3d GetWorldPos3D()
- `Vessel.cs:3993` public void Load()
- `Vessel.cs:4133` public void Unload()
- `Vessel.cs:4398` public ProtoVessel BackupVessel()
- `Vessel.cs:5527` public void GoOnRails()
- `Vessel.cs:5624` public void GoOffRails()
- `Vessel.cs:8607` public void SetWorldVelocity(Vector3d vel)
- `Vessel.cs:10095` public void SetPosition(Vector3d position)
- `Vessel.cs:10100` public void SetPosition(Vector3d position, bool usePristineCoords)
- `Vessel.cs:10188` public void SetRotation(Quaternion rotation)
- `Vessel.cs:10193` public void SetRotation(Quaternion rotation, bool setPos)

## Vessel.Situations
- `Vessel.cs:29` LANDED = 1,
- `Vessel.cs:31` SPLASHED = 2,
- `Vessel.cs:33` PRELAUNCH = 4,
- `Vessel.cs:35` FLYING = 8,
- `Vessel.cs:37` SUB_ORBITAL = 0x10,
- `Vessel.cs:39` ORBITING = 0x20,
- `Vessel.cs:41` ESCAPING = 0x40,
- `Vessel.cs:43` DOCKED = 0x80

## FlightGlobals vessels/RemoveVessel/ActiveVessel/ship_* 
- `FlightGlobals.cs:22` public static bool ready;
- `FlightGlobals.cs:132` public static List<CelestialBody> Bodies => fetch.bodies;
- `FlightGlobals.cs:167` public static Vessel ActiveVessel => fetch.activeVessel;
- `FlightGlobals.cs:243` public static List<Vessel> Vessels => fetch.vessels;
- `FlightGlobals.cs:245` public static List<Vessel> VesselsLoaded => fetch.vesselsLoaded;
- `FlightGlobals.cs:247` public static List<Vessel> VesselsUnloaded => fetch.vesselsUnloaded;
- `FlightGlobals.cs:261` public static Vector3d ship_velocity => ActiveVessel.rb_velocity;
- `FlightGlobals.cs:485` public static void RemoveVessel(Vessel vessel)
- `FlightGlobals.cs:671` public static Vessel FindVessel(Guid id)
- `FlightGlobals.cs:3305` public static CelestialBody getMainBody(Vector3d refPos)
- `FlightGlobals.cs:3358` public static CelestialBody getMainBody()

## FlightState.protoVessels
- `FlightState.cs:17` public List<ProtoVessel> protoVessels;
- `FlightState.cs:27` public double universalTime;

## OrbitDriver updateMode/UpdateMode/updateFromParameters/pos/vel
- `OrbitDriver.cs:6` public enum UpdateMode
- `OrbitDriver.cs:8` TRACK_Phys,
- `OrbitDriver.cs:9` UPDATE,
- `OrbitDriver.cs:10` IDLE
- `OrbitDriver.cs:15` public Vector3d pos;
- `OrbitDriver.cs:17` public Vector3d vel;
- `OrbitDriver.cs:21` public Orbit orbit = new Orbit();
- `OrbitDriver.cs:33` public UpdateMode updateMode;
- `OrbitDriver.cs:39` public Vessel vessel;
- `OrbitDriver.cs:63` public CelestialBody referenceBody
- `OrbitDriver.cs:124` case UpdateMode.TRACK_Phys:
- `OrbitDriver.cs:125` case UpdateMode.IDLE:
- `OrbitDriver.cs:145` case UpdateMode.UPDATE:
- `OrbitDriver.cs:262` case UpdateMode.TRACK_Phys:
- `OrbitDriver.cs:263` case UpdateMode.IDLE:
- `OrbitDriver.cs:331` case UpdateMode.UPDATE:

## Orbit UpdateFromStateVectors/getPositionAtUT/getOrbitalVelocityAtUT/elements
- `Orbit.cs:108` public CelestialBody referenceBody;
- `Orbit.cs:110` public double inclination;
- `Orbit.cs:112` public double eccentricity;
- `Orbit.cs:114` public double semiMajorAxis;
- `Orbit.cs:116` public double LAN;
- `Orbit.cs:118` public double argumentOfPeriapsis;
- `Orbit.cs:120` public double epoch;
- `Orbit.cs:165` public double meanAnomalyAtEpoch;
- `Orbit.cs:306` public Orbit()
- `Orbit.cs:310` public Orbit(double inc, double e, double sma, double lan, double argPe, double mEp, double t, CelestialBody body)
- `Orbit.cs:315` public Orbit(Orbit orbit)
- `Orbit.cs:526` public void UpdateFromStateVectors(Vector3d pos, Vector3d vel, CelestialBody refBody, double UT)
- `Orbit.cs:965` public Vector3d getPositionAtUT(double UT)
- `Orbit.cs:970` public Vector3d getTruePositionAtUT(double UT)
- `Orbit.cs:975` public Vector3d getRelativePositionAtUT(double UT)
- `Orbit.cs:980` public Vector3d getOrbitalVelocityAtUT(double UT)

## CelestialBody GetWorldSurfacePosition/position/rotation/bodyTransform/flightGlobalsIndex
- `CelestialBody.cs:22` public double Radius;
- `CelestialBody.cs:265` public Transform bodyTransform;
- `CelestialBody.cs:330` public int flightGlobalsIndex { get; set; }
- `CelestialBody.cs:332` public Vector3d position
- `CelestialBody.cs:1342` public Vector3d GetSurfaceNVector(double lat, double lon)
- `CelestialBody.cs:1350` public Vector3d GetRelSurfacePosition(double lat, double lon, double alt)
- `CelestialBody.cs:1355` public Vector3d GetRelSurfacePosition(double lat, double lon, double alt, out Vector3d normal)
- `CelestialBody.cs:1361` public Vector3d GetRelSurfacePosition(Vector3d worldPosition)
- `CelestialBody.cs:1371` public Vector3d GetWorldSurfacePosition(double lat, double lon, double alt)
- `CelestialBody.cs:1376` public double GetLatitude(Vector3d pos, bool isRadial = false)
- `CelestialBody.cs:1419` public double GetLongitude(Vector3d pos, bool isRadial = false)
- `CelestialBody.cs:1554` public double GetAltitude(Vector3d worldPos)
- `CelestialBody.cs:1559` public void GetLatLonAlt(Vector3d worldPos, out double lat, out double lon, out double alt)

## Part: rb/partTransform/ResumeVelocity/vel/flightID/crashTolerance/vessel/parent/children/Modules
- `Part.cs:139` public Vessel vessel;
- `Part.cs:180` public uint flightID;
- `Part.cs:186` public Part parent;
- `Part.cs:190` public List<Part> children = new List<Part>();
- `Part.cs:192` public Transform partTransform;
- `Part.cs:263` public List<ProtoCrewMember> protoModuleCrew = new List<ProtoCrewMember>();
- `Part.cs:293` public Vector3 orgPos;
- `Part.cs:295` public Quaternion orgRot;
- `Part.cs:313` public float crashTolerance = 9f;
- `Part.cs:457` public Rigidbody rb;
- `Part.cs:461` public bool packed;
- `Part.cs:615` public Vector3 vel;
- `Part.cs:1399` public PartModuleList Modules => modules;
- `Part.cs:9085` public void Pack()
- `Part.cs:9202` public void Unpack()
- `Part.cs:9331` public void ResumeVelocity()

## FlightIntegrator / CollisionEnhancer / PartBuoyancy classes
- `FlightIntegrator.cs:5` public class FlightIntegrator : VesselModule

## CollisionEnhancer
- `CollisionEnhancer.cs:4` public class CollisionEnhancer : MonoBehaviour

## PartBuoyancy
- `PartBuoyancy.cs:6` public class PartBuoyancy : MonoBehaviour

## KSCVesselMarkers.RefreshMarkers
- `KSP/UI/Screens/KSCVesselMarkers.cs:10` public static KSCVesselMarkers fetch;
- `KSP/UI/Screens/KSCVesselMarkers.cs:173` RefreshMarkers();
- `KSP/UI/Screens/KSCVesselMarkers.cs:176` public void RefreshMarkers()

## FloatingOrigin / Krakensbane
- `FloatingOrigin.cs:32` public static FloatingOrigin fetch;
- `FloatingOrigin.cs:46` public static Vector3d Offset
- `FloatingOrigin.cs:96` public static Vector3d OffsetNonKrakensbane
- `FloatingOrigin.cs:829` public static bool RegisterParticleSystem(ParticleSystem sys)
- `FloatingOrigin.cs:865` public static bool UnregisterParticleSystem(ParticleSystem sys)

## Krakensbane.GetFrameVelocity
- `Krakensbane.cs:572` public static Vector3d GetFrameVelocity()
- `Krakensbane.cs:582` public static Vector3d GetLastCorrection()

## VesselRanges / PhysicsGlobals
- `VesselRanges.cs:9` public float load;
- `VesselRanges.cs:11` public float unload;
- `VesselRanges.cs:13` public float pack;
- `VesselRanges.cs:15` public float unpack;
- `VesselRanges.cs:130` public Situation prelaunch = new Situation(2250f, 2500f, 350f, 200f);
- `VesselRanges.cs:132` public Situation landed = new Situation(2250f, 2500f, 350f, 200f);
- `VesselRanges.cs:134` public Situation splashed = new Situation(2250f, 2500f, 350f, 200f);
- `VesselRanges.cs:136` public Situation flying = new Situation(2250f, 22500f, 25000f, 2000f);
- `VesselRanges.cs:138` public Situation subOrbital = new Situation(2250f, 15000f, 10000f, 200f);
- `VesselRanges.cs:140` public Situation orbit = new Situation(2250f, 2500f, 350f, 200f);
- `VesselRanges.cs:142` public Situation escaping = new Situation(2250f, 2500f, 350f, 200f);

## GameEvents vessel lifecycle
- `GameEvents.cs:286` public static EventData<Vessel> onNewVesselCreated = new EventData<Vessel>("onNewVesselCreated");
- `GameEvents.cs:298` public static EventData<Vessel> onVesselWillDestroy = new EventData<Vessel>("onVesselWillDestroy");
- `GameEvents.cs:300` public static EventData<Vessel> onVesselCreate = new EventData<Vessel>("onVesselCreate");
- `GameEvents.cs:302` public static EventData<Vessel> onVesselDestroy = new EventData<Vessel>("onVesselDestroy");
- `GameEvents.cs:306` public static EventData<Vessel> onVesselChange = new EventData<Vessel>("onVesselChange");
- `GameEvents.cs:308` public static EventData<Vessel, Vessel> onVesselSwitching = new EventData<Vessel, Vessel>("onVesselSwitching");
- `GameEvents.cs:322` public static EventData<Vessel> onVesselGoOnRails = new EventData<Vessel>("onVesselGoOnRails");
- `GameEvents.cs:324` public static EventData<Vessel> onVesselGoOffRails = new EventData<Vessel>("onVesselGoOffRails");
- `GameEvents.cs:330` public static EventData<Vessel> onVesselLoaded = new EventData<Vessel>("onVesselLoaded");
- `GameEvents.cs:332` public static EventData<Vessel> onVesselUnloaded = new EventData<Vessel>("onVesselUnloaded");
- `GameEvents.cs:336` public static EventData<Vessel> onVesselWasModified = new EventData<Vessel>("onVesselWasModified");
- `GameEvents.cs:338` public static EventData<Vessel> onVesselPartCountChanged = new EventData<Vessel>("onVesselPartCountChanged");
- `GameEvents.cs:352` public static EventData<ProtoVessel, bool> onVesselRecovered = new EventData<ProtoVessel, bool>("onVesselRecovered");
- `GameEvents.cs:354` public static EventData<ProtoVessel> onVesselTerminated = new EventData<ProtoVessel>("onVesselTerminated");
- `GameEvents.cs:877` public static EventData<ProtoVessel, MissionRecoveryDialog, float> onVesselRecoveryProcessing = new EventData<ProtoVessel, MissionRecoveryDialog, float>("onVesselRecovery

## Vessel.protoVessel / vesselModules
- `Vessel.cs:154` public ProtoVessel protoVessel;
- `Vessel.cs:387` public List<VesselModule> vesselModules;
- `Vessel.cs:882` public bool isActiveVessel => FlightGlobals.ActiveVessel == this;
- `Vessel.cs:1111` public bool IsControllable => isControllable;
- `Vessel.cs:4637` public void MakeActive()
- `Vessel.cs:4702` public void MakeInactive()

## ProtoPartSnapshot: flightID/persistentId/protoCrewNames/partName/craftID
- `ProtoPartSnapshot.cs:18` public string partName;
- `ProtoPartSnapshot.cs:20` public uint craftID;
- `ProtoPartSnapshot.cs:22` public uint flightID;
- `ProtoPartSnapshot.cs:28` public uint persistentId;
- `ProtoPartSnapshot.cs:74` public List<ProtoCrewMember> protoModuleCrew;
- `ProtoPartSnapshot.cs:78` public List<string> protoCrewNames;
- `ProtoPartSnapshot.cs:104` public List<ProtoPartModuleSnapshot> modules;

