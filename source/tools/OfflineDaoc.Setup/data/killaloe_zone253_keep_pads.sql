-- Move region 253 Keep rows from the Abermenai pads onto the captured Killaloe spots.
-- Source: owner's live 0x69 keep packets. Local X/Y already have retail offset 524288
-- removed. 0x69 Z was 0. Z is the captured courtyard floor for portal keeps and the
-- huscarl floor for Dun Killaloe. Heading is left as imported so the keep template is
-- not rotated without an in-game look. KeepComponent X/Y are keep-relative and move
-- with Keep X/Y. Those rows are not rewritten. Abermenai Door InternalIDs are deleted
-- so they do not load beside the captured Killaloe doors. Hibernia's keep gate was not
-- in the capture and is not invented. Tests only until seen in game.

UPDATE Keep SET X=20536, Y=57944, Z=8288 WHERE Region=253 AND (KeepID=37 OR (X=37301 AND Y=52362));
UPDATE Keep SET X=60657, Y=35209, Z=8288 WHERE Region=253 AND (KeepID=36 OR (X=54053 AND Y=24680));
UPDATE Keep SET X=10963, Y=13279, Z=8288 WHERE Region=253 AND (KeepID=35 OR (X=18362 AND Y=18257));
UPDATE Keep SET X=32779, Y=32612, Z=8768 WHERE Region=253 AND (KeepID=33 OR (X=33089 AND Y=38271));

DELETE FROM Door WHERE InternalID IN (253000101, 253000102, 253000201, 253000202, 253000301, 253000302, 253041601, 253041602);

UPDATE Area SET X=20536, Y=57944, Z=8288 WHERE Region=253 AND X=37301 AND Y=52362;
UPDATE Area SET X=60657, Y=35209, Z=8288 WHERE Region=253 AND X=54053 AND Y=24680;
UPDATE Area SET X=10963, Y=13279, Z=8288 WHERE Region=253 AND X=18362 AND Y=18257;
UPDATE Area SET X=32779, Y=32612, Z=8768 WHERE Region=253 AND X=33089 AND Y=38271;
