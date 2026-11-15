-- Killaloe playable follow-up. Applied on the next GameServer start.
-- Does not retarget NPCs onto Abermenai pads. Does not invent a Hibernia keep gate.
-- Does not copy Thidranki placeholder guards.
-- Region 253 zone offset is 1,1 (8192). Captured X/Y are zone-local 0..65536 (retail 237 minus 524288).
-- Fixtures live in that local square. The client subtracts 8192, so staff stored as local sit 204 m off the keeps.

-- Dun Killaloe starts Midgard-owned (captured staff are Midgard). Portal keeps already have a realm.
UPDATE Keep SET Realm=2, OriginalRealm=2 WHERE Region=253 AND KeepID=33;

-- Door table realm: eRealm.Door (6) is freely usable if DoorMgr falls back to GameDoor.
UPDATE Door SET Realm=1 WHERE Door_ID LIKE 'offline-killaloe-door-%' AND InternalID BETWEEN 253201000 AND 253201999;
UPDATE Door SET Realm=2 WHERE Door_ID LIKE 'offline-killaloe-door-%' AND InternalID BETWEEN 253202000 AND 253202999;
UPDATE Door SET Realm=3 WHERE Door_ID LIKE 'offline-killaloe-door-%' AND InternalID BETWEEN 253203000 AND 253203999;
UPDATE Door SET Realm=2 WHERE Door_ID LIKE 'offline-killaloe-door-%' AND InternalID BETWEEN 253138000 AND 253138999;

-- Combat keep staff use realm-class guards so AUTOMODEL picks Albion/Midgard/Hibernia races,
-- not DefaultCharacterClass's all-race list. Do not undo Dun Killaloe Realm=2.
UPDATE Mob SET ClassType='DOL.GS.Keeps.GuardFighter'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253
   AND (Name IN ('Knight Commander','Viking Jarl','Champion','Huscarl','Albion Commander','Midgard Commander','Hibernia Commander')
        OR Name LIKE 'Captain %');
UPDATE Mob SET ClassType='DOL.GS.Keeps.GuardArcher'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253
   AND Name IN ('Bowman Commander','Master Hunter','Master Ranger');
UPDATE Mob SET ClassType='DOL.GS.Keeps.GuardCaster'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253
   AND Name IN ('Master Wizard','Master of Runes','Master Eldritch');
UPDATE Mob SET ClassType='DOL.GS.Keeps.GuardLord'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253 AND Name='Jarl Killaloe';
UPDATE Mob SET ClassType='DOL.GS.Keeps.GateKeeperIn'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253
   AND (Name LIKE '%Enter Gatekeeper%' OR Name LIKE '%Enter PosternKeeper%');
UPDATE Mob SET ClassType='DOL.GS.Keeps.GateKeeperOut'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253
   AND (Name LIKE '%Exit Gatekeeper%' OR Name LIKE '%Exit PosternKeeper%');

-- Siege and bounty merchants must load as merchants with a trade list.
UPDATE Mob SET ClassType='DOL.GS.GameMerchant', ItemsListTemplateID='SiegeMerchantAlb'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253 AND Guild='Merchant' AND Realm=1;
UPDATE Mob SET ClassType='DOL.GS.GameMerchant', ItemsListTemplateID='SiegeMerchantMid'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253 AND Guild='Merchant' AND Realm=2;
UPDATE Mob SET ClassType='DOL.GS.GameMerchant', ItemsListTemplateID='SiegeMerchantHib'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253 AND Guild='Merchant' AND Realm=3;
UPDATE Mob SET ClassType='DOL.GS.GameBountyMerchant', ItemsListTemplateID='AlbBountyMerchant'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253 AND Guild='Bounty Merchant' AND Realm=1;
UPDATE Mob SET ClassType='DOL.GS.GameBountyMerchant', ItemsListTemplateID='MidBountyStore'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253 AND Guild='Bounty Merchant' AND Realm=2;
UPDATE Mob SET ClassType='DOL.GS.GameBountyMerchant', ItemsListTemplateID='HibBountyMerchant'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253 AND Guild='Bounty Merchant' AND Realm=3;

-- Hastener becomes the realm bard. Captured models already match GuardTemplateMgr (244/22/1910).
UPDATE Mob SET ClassType='DOL.GS.GameHastener', Name='Minstrel of Albion', Guild='Hastener', Model=244
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253 AND Name='Hastener' AND Realm=1;
UPDATE Mob SET ClassType='DOL.GS.GameHastener', Name='Skald of Midgard', Guild='Hastener', Model=22
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253 AND Name='Hastener' AND Realm=2;
UPDATE Mob SET ClassType='DOL.GS.GameHastener', Name='Bard of Hibernia', Guild='Hastener', Model=1910
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253 AND Name='Hastener' AND Realm=3;

-- Channeler: captured Prydwen Keep Channeler. Hibernia town templates in this client are
-- Channeler Glasny, not Grove Tender, so Hibernia uses Channeler.
UPDATE Mob SET ClassType='DOL.GS.AlbionTeleporter', Guild='Channeler'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253 AND Guild LIKE '%Keep Channeler%' AND Realm=1;
UPDATE Mob SET ClassType='DOL.GS.MidgardTeleporter', Guild='Stor Gothi'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253 AND Guild LIKE '%Keep Channeler%' AND Realm=2;
UPDATE Mob SET ClassType='DOL.GS.HiberniaTeleporter', Guild='Channeler'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253 AND Guild LIKE '%Keep Channeler%' AND Realm=3;

-- Owner does not want realm-enhancement clerics live yet.
DELETE FROM Mob WHERE PackageID='offline-killaloe-keep-253' AND Region=253 AND Guild='Realm Enhancement';

-- Field pack: drop OpenDAoC templates that ReplaceMobValues and zero AggroLevel.
-- Snap Z to zone 253 heights.f32 at the dump XY. Do not drop a cliff spawn into a nearby
-- valley while leaving X/Y on the peak: that hangs the mob in canyon air between the cliffs.
-- Fix Speed 8180. Set roam where the dump stored -1.

UPDATE Mob SET Z=8208, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032783' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=9888, Speed=180, AggroLevel=0, RoamingRange=120, NPCTemplateID=-1 WHERE Mob_ID='100032784' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8816, Speed=180, AggroLevel=200, RoamingRange=120, NPCTemplateID=-1 WHERE Mob_ID='100032785' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032786' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032787' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8040, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032788' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032789' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032790' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032791' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8736, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032792' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8080, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032793' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8008, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032794' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8008, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032795' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8296, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032796' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8136, Speed=180, AggroLevel=200, RoamingRange=120, NPCTemplateID=-1 WHERE Mob_ID='100032797' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8000, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032798' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8080, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032799' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8120, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032800' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8200, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032801' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7928, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032802' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032803' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7928, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032804' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032805' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8120, Speed=180, AggroLevel=200, RoamingRange=120, NPCTemplateID=-1 WHERE Mob_ID='100032806' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032807' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8144, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032808' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8000, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032809' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8184, Speed=180, AggroLevel=200, RoamingRange=120, NPCTemplateID=-1 WHERE Mob_ID='100032810' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032811' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8008, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032812' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8024, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032813' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8008, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032814' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7928, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032815' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032816' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8168, Speed=180, AggroLevel=200, RoamingRange=120, NPCTemplateID=-1 WHERE Mob_ID='100032817' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032818' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8120, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032819' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8008, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032820' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8368, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032821' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8776, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032822' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032823' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8824, Speed=180, AggroLevel=200, RoamingRange=120, NPCTemplateID=-1 WHERE Mob_ID='100032824' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8736, Speed=180, AggroLevel=200, RoamingRange=120, NPCTemplateID=-1 WHERE Mob_ID='100032825' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8088, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032826' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8016, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032827' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8000, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032828' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7960, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032829' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8512, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032830' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8016, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032831' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8008, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032832' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7960, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032833' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8216, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032834' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7944, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032835' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032836' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8000, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032837' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032838' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8968, Speed=180, AggroLevel=80, RoamingRange=350, NPCTemplateID=-1 WHERE Mob_ID='100032839' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8008, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032840' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8392, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032841' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8000, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032842' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032843' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8008, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032844' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8240, Speed=180, AggroLevel=200, RoamingRange=120, NPCTemplateID=-1 WHERE Mob_ID='100032845' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032846' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8344, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032847' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032848' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8024, Speed=180, AggroLevel=200, RoamingRange=120, NPCTemplateID=-1 WHERE Mob_ID='100032849' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8152, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032850' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8008, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032851' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8000, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032852' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032853' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8328, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032854' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=7920, Speed=180, AggroLevel=200, RoamingRange=250, NPCTemplateID=-1 WHERE Mob_ID='100032855' AND PackageID='offline-killaloe-zone253';
UPDATE Mob SET Z=8080, Speed=180, AggroLevel=200, RoamingRange=200, NPCTemplateID=-1 WHERE Mob_ID='100032856' AND PackageID='offline-killaloe-zone253';

-- Region 253's zone starts at sector 1,1. Add that 8192 to keep staff, doors and Keep
-- rows so client to_local matches keep meshes. Do not add it to the OpenDAoC field pack:
-- those dump X/Y, once the client subtracts 8192, sit on the Albion-portal to Dun Killaloe
-- mesh path (forest cat 100032792 displays 7.7 m off that road). Offsetting the field pack
-- moved it 204 m off the road, which emptied the path after the keep-mesh start.
UPDATE Mob SET X=X+8192, Y=Y+8192 WHERE PackageID='offline-killaloe-keep-253'
 AND Region=253 AND X BETWEEN 0 AND 65536 AND Y BETWEEN 0 AND 65536;
UPDATE Door SET X=X+8192, Y=Y+8192 WHERE Door_ID LIKE 'offline-killaloe-door-%'
 AND X BETWEEN 0 AND 65536 AND Y BETWEEN 0 AND 65536;
UPDATE Keep SET X=X+8192, Y=Y+8192 WHERE Region=253 AND X BETWEEN 0 AND 65536 AND Y BETWEEN 0 AND 65536;
-- Area rows are not reset by the pad SQL on later starts; pin them to the offset spots.
UPDATE Area SET X=28728, Y=66136, Z=8288 WHERE Region=253 AND X IN (37301, 20536, 28728) AND Y IN (52362, 57944, 66136);
UPDATE Area SET X=68849, Y=43401, Z=8288 WHERE Region=253 AND X IN (54053, 60657, 68849) AND Y IN (24680, 35209, 43401);
UPDATE Area SET X=19155, Y=21471, Z=8288 WHERE Region=253 AND X IN (18362, 10963, 19155) AND Y IN (18257, 13279, 21471);
UPDATE Area SET X=40971, Y=40804, Z=8768 WHERE Region=253 AND X IN (33089, 32779, 40971) AND Y IN (38271, 32612, 40804);

