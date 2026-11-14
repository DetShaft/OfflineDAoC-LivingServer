-- Killaloe keep gate, gatehouse, and postern doors for zone 253.
-- Source: owner's live capture of retail Killaloe (region 237), local coords already
-- minus 524288 (64*8192). InternalID millions place is 253 so DoorMgr loads them
-- in this region's zone. Hibernia's keep gate was not in the capture and is not invented.
-- Positions are tests-only until seen in game.

REPLACE INTO `door` (`Name`, `Type`, `Z`, `Y`, `X`, `Heading`, `InternalID`, `Guild`, `Level`, `Realm`, `Flags`, `Locked`, `Health`, `LastTimeRowUpdated`, `Door_ID`, `IsPostern`, `State`) VALUES
	('Keep Gate', 2, 8288, 56709, 20564, 2184, 253201001, NULL, 20, 6, 0, 0, 51000, '2000-01-01 00:00:00', 'offline-killaloe-door-00b7', 0, 1),
	('Gatehouse Door', 2, 8720, 56851, 20668, 2969, 253201002, NULL, 20, 6, 0, 0, 2545, '2000-01-01 00:00:00', 'offline-killaloe-door-00b8', 0, 0),
	('Gatehouse Door', 2, 8720, 56826, 20443, 1103, 253201003, NULL, 20, 6, 0, 0, 2545, '2000-01-01 00:00:00', 'offline-killaloe-door-00b9', 0, 0),
	('Keep Gate', 2, 8288, 35287, 59425, 1103, 253202001, NULL, 20, 6, 0, 0, 51000, '2000-01-01 00:00:00', 'offline-killaloe-door-00bc', 0, 1),
	('Gatehouse Door', 2, 8720, 35171, 59550, 2036, 253202002, NULL, 20, 6, 0, 0, 2545, '2000-01-01 00:00:00', 'offline-killaloe-door-00bd', 0, 0),
	('Gatehouse Door', 2, 8720, 35398, 59551, 22, 253202003, NULL, 20, 6, 0, 0, 2545, '2000-01-01 00:00:00', 'offline-killaloe-door-00be', 0, 0),
	('Gatehouse Door', 2, 8720, 14004, 11799, 432, 253203002, NULL, 20, 6, 0, 0, 2545, '2000-01-01 00:00:00', 'offline-killaloe-door-00c2', 0, 0),
	('Gatehouse Door', 2, 8720, 13827, 11941, 2514, 253203003, NULL, 20, 6, 0, 0, 2545, '2000-01-01 00:00:00', 'offline-killaloe-door-00c3', 0, 0),
	('Keep Gate', 2, 8768, 33843, 32557, 79, 253138001, NULL, 20, 6, 0, 0, 51000, '2000-01-01 00:00:00', 'offline-killaloe-door-011d', 0, 1),
	('Gatehouse Door', 2, 9200, 33718, 32441, 1012, 253138003, NULL, 20, 6, 0, 0, 2545, '2000-01-01 00:00:00', 'offline-killaloe-door-013a', 0, 0),
	('Gatehouse Door', 2, 9200, 33717, 32668, 3094, 253138004, NULL, 20, 6, 0, 0, 2545, '2000-01-01 00:00:00', 'offline-killaloe-door-0150', 0, 0),
	('Keep Gate', 2, 8770, 32033, 32783, 0, 253138002, NULL, 20, 6, 0, 0, 51000, '2000-01-01 00:00:00', 'offline-killaloe-door-01a5', 0, 1),
	('Postern Door', 2, 9346, 31790, 32741, 11, 253138011, NULL, 20, 6, 0, 0, 2545, '2000-01-01 00:00:00', 'offline-killaloe-door-01b3', 1, 0),
	('Postern Door', 2, 9158, 31334, 32252, 989, 253138012, NULL, 20, 6, 0, 0, 2545, '2000-01-01 00:00:00', 'offline-killaloe-door-01c0', 1, 0),
	('Postern Door', 2, 8853, 31335, 33335, 3072, 253138013, NULL, 20, 6, 0, 0, 2545, '2000-01-01 00:00:00', 'offline-killaloe-door-01cd', 1, 0),
	('Postern Door', 2, 9177, 31343, 33291, 3037, 253138014, NULL, 20, 6, 0, 0, 2545, '2000-01-01 00:00:00', 'offline-killaloe-door-01d3', 1, 0),
	('Postern Door', 2, 8850, 31327, 32221, 978, 253138015, NULL, 20, 6, 0, 0, 2545, '2000-01-01 00:00:00', 'offline-killaloe-door-01d6', 1, 0);
