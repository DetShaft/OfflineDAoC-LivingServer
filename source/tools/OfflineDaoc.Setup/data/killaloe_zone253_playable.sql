-- Killaloe playable follow-up. Applied on the next GameServer start.
-- Does not retarget NPCs onto Abermenai pads. Does not invent a Hibernia keep gate.
-- Does not copy Thidranki placeholder guards.

-- Dun Killaloe starts Midgard-owned (captured staff are Midgard). Portal keeps already have a realm.
UPDATE Keep SET Realm=2, OriginalRealm=2 WHERE Region=253 AND KeepID=33;

-- Door table realm: eRealm.Door (6) is freely usable if DoorMgr falls back to GameDoor.
UPDATE Door SET Realm=1 WHERE Door_ID LIKE 'offline-killaloe-door-%' AND InternalID BETWEEN 253201000 AND 253201999;
UPDATE Door SET Realm=2 WHERE Door_ID LIKE 'offline-killaloe-door-%' AND InternalID BETWEEN 253202000 AND 253202999;
UPDATE Door SET Realm=3 WHERE Door_ID LIKE 'offline-killaloe-door-%' AND InternalID BETWEEN 253203000 AND 253203999;
UPDATE Door SET Realm=2 WHERE Door_ID LIKE 'offline-killaloe-door-%' AND InternalID BETWEEN 253138000 AND 253138999;

-- Combat keep staff become GameKeepGuard so they attach to the keep area and count as guards.
-- Peaceful keep NPCs (hastener, named civilians) stay GameNPC.
UPDATE Mob SET ClassType='DOL.GS.Keeps.GameKeepGuard'
 WHERE PackageID='offline-killaloe-keep-253' AND Region=253
   AND (Flags=0 OR Name LIKE '%Gatekeeper%' OR Name LIKE '%PosternKeeper%' OR Name LIKE 'Captain %'
        OR Name LIKE '% Commander' OR Name='Jarl Killaloe' OR Name='Huscarl');

-- Field pack: drop OpenDAoC templates that ReplaceMobValues and zero AggroLevel.
-- Snap Z to zone 253 heights.f32. Fix Speed 8180. Set roam where the dump stored -1.

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
