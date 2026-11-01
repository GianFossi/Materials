PRAGMA foreign_keys = ON;
BEGIN;

-- 1. Identità del bullone e minimi scalari.
INSERT INTO Main (Specification, Grade, ClassConditionTemper, AlloyDesignation, NominalComposition, SMYS, SMUS)
VALUES ('SA-DEMO3', 'B1', 'QT', 'G99999', '1Cr-0.5Mo bolting (demo)', 720, 860);
INSERT INTO Material_Identity (id_main, NaturalKey)
VALUES (last_insert_rowid(), '["SA-DEMO3","B1","QT","G99999"]');

-- 2. Intervalli di diametro d (mm): tre fasce.
CREATE TEMP TABLE new_size (label TEXT PRIMARY KEY, id INTEGER NOT NULL);
INSERT INTO Sizes (SizeMin, SizeMinIncluded, SizeMax, SizeMaxIncluded) VALUES (NULL, 0, 65.0, 1);    -- d <= 65
INSERT INTO new_size VALUES ('le65', last_insert_rowid());
INSERT INTO Sizes (SizeMin, SizeMinIncluded, SizeMax, SizeMaxIncluded) VALUES (65.0, 0, 100.0, 1);   -- 65 < d <= 100
INSERT INTO new_size VALUES ('65_100', last_insert_rowid());
INSERT INTO Sizes (SizeMin, SizeMinIncluded, SizeMax, SizeMaxIncluded) VALUES (100.0, 0, 150.0, 1);  -- 100 < d <= 150
INSERT INTO new_size VALUES ('100_150', last_insert_rowid());

-- 3. Curve: Sy e Su, Sb1 (Table 3, Division 1) e Sb2 (Table 4, Division 2), ciascuna sulle tre fasce di diametro.
CREATE TEMP TABLE demo_curve (key TEXT PRIMARY KEY, code TEXT NOT NULL, size_label TEXT NOT NULL, src TEXT NOT NULL, id INTEGER, size_id INTEGER);
INSERT INTO demo_curve (key, code, size_label, src) VALUES
    ('sy_le65', 'Sy', 'le65', 'Sy'), ('sy_65_100', 'Sy', '65_100', 'Sy'), ('sy_100_150', 'Sy', '100_150', 'Sy'),
    ('su_le65', 'Su', 'le65', 'Su'), ('su_65_100', 'Su', '65_100', 'Su'), ('su_100_150', 'Su', '100_150', 'Su'),
    ('sb1_le65', 'Sb1', 'le65', '3'), ('sb1_65_100', 'Sb1', '65_100', '3'), ('sb1_100_150', 'Sb1', '100_150', '3'),
    ('sb2_le65', 'Sb2', 'le65', '4'), ('sb2_65_100', 'Sb2', '65_100', '4'), ('sb2_100_150', 'Sb2', '100_150', '4');
UPDATE demo_curve SET size_id = (SELECT id FROM new_size WHERE label = size_label);

-- ProductForm resta NULL: il CHECK della tabella lo ammette solo per S1/S2.
INSERT INTO Material_Properties (id_main, id_size, PropertyCode)
SELECT m.id, c.size_id, c.code
FROM demo_curve c JOIN Main m ON m.Specification = 'SA-DEMO3' AND m.Grade = 'B1'
ORDER BY c.rowid;
UPDATE demo_curve
SET id = (SELECT MIN(p.id) FROM Material_Properties p JOIN Main m ON m.id = p.id_main
          WHERE m.Specification = 'SA-DEMO3' AND m.Grade = 'B1') + rowid - 1;

-- 4. Punti (T in °C, valori in MPa).
CREATE TEMP TABLE demo_temp (T REAL PRIMARY KEY);
INSERT INTO demo_temp VALUES (40), (100), (150), (200), (250), (300), (350), (400), (450), (500);
CREATE TEMP TABLE demo_vals (key TEXT PRIMARY KEY, v40, v100, v150, v200, v250, v300, v350, v400, v450, v500);
INSERT INTO demo_vals VALUES
    ('sy_le65',   725, 700, 685, 670, 650, 630, 605, 570, 520, 450), ('sy_65_100',  690, 668, 655, 640, 620, 600, 575, 540, 490, 425),
    ('sy_100_150',655, 635, 620, 608, 590, 570, 545, 510, 465, 400),
    ('su_le65',   860, 860, 850, 840, 830, 810, 780, 730, 660, 570), ('su_65_100',  825, 825, 815, 805, 795, 778, 750, 700, 635, 548),
    ('su_100_150',790, 790, 780, 770, 760, 745, 718, 670, 608, 525),
    ('sb1_le65',  172, 172, 172, 172, 172, 172, 172, 169, 121, 77), ('sb1_65_100', 161, 161, 161, 161, 161, 161, 161, 160, 118, 75),
    ('sb1_100_150',138,138, 138, 138, 138, 138, 138, 138, 112, 70),
    ('sb2_le65',  207, 207, 207, 207, 207, 207, 205, 185, 140, 90), ('sb2_65_100', 193, 193, 193, 193, 193, 193, 191, 176, 135, 88),
    ('sb2_100_150',172,172, 172, 172, 172, 172, 170, 160, 128, 82);
INSERT INTO Property_Values (id_property, Temperature, StressValue)
SELECT c.id, t.T,
       CASE t.T WHEN 40 THEN v.v40 WHEN 100 THEN v.v100 WHEN 150 THEN v.v150 WHEN 200 THEN v.v200 WHEN 250 THEN v.v250
                WHEN 300 THEN v.v300 WHEN 350 THEN v.v350 WHEN 400 THEN v.v400 WHEN 450 THEN v.v450 ELSE v.v500 END
FROM demo_curve c JOIN demo_vals v ON v.key = c.key CROSS JOIN demo_temp t;

-- 5. Fonte PRIMA di Sb1/Sb2: il trigger Validate_Sb1_Insert accetta solo curve Sb1 con fonte Table 3 (Sb2: Table 4),
--    stesso materiale e stesso intervallo. SizeAxis 'diameter' dichiara che l'intervallo è un diametro.
INSERT INTO Property_Source (id_property, SourceTable, File, CsvLine, Page, SizeAxis, MinYieldMPa, MinTensileMPa, RawFieldsJson, Fingerprint)
SELECT id, src, 'User/demo-' || key, 1, 'demo', 'diameter', CASE WHEN code = 'Su' THEN NULL ELSE 720 END, 860, '{}', 'User'
FROM demo_curve;

INSERT INTO Sb1 (id_property, id_main, id_size) SELECT id, (SELECT id FROM Main WHERE Specification = 'SA-DEMO3'), size_id FROM demo_curve WHERE code = 'Sb1';
INSERT INTO Sb2 (id_property, id_main, id_size) SELECT id, (SELECT id FROM Main WHERE Specification = 'SA-DEMO3'), size_id FROM demo_curve WHERE code = 'Sb2';

-- 6. Metadati: asse diametro e limiti per Section (VIII-1 per Sb1, VIII-2 per Sb2).
INSERT INTO Property_Curve_Metadata (Code, CurveId, SizeAxis, CodeLimitsJson)
SELECT code, id, 'diameter',
       CASE code
            WHEN 'Sb1' THEN '{"PNumber":"","GroupNumber":"","ExternalPressureChart":"","MaxTempSectionI":"","MaxTempSectionIII":"427","MaxTempSectionVIII1":"427","MaxTempSectionVIII2":"","MaxTempSectionXII":"427"}'
            WHEN 'Sb2' THEN '{"PNumber":"","GroupNumber":"","ExternalPressureChart":"","MaxTempSectionI":"","MaxTempSectionIII":"","MaxTempSectionVIII1":"","MaxTempSectionVIII2":"400","MaxTempSectionXII":""}'
       END
FROM demo_curve;

-- 7. Note ufficiali: 3/W1 (saldatura non ammessa) per Sb1, 4/G1 (rilassamento oltre 250 °C) per Sb2.
INSERT INTO Property_Note_Links (id_property, SourceTable, Code) SELECT id, '3', 'W1' FROM demo_curve WHERE code = 'Sb1';
INSERT INTO Property_Note_Links (id_property, SourceTable, Code) SELECT id, '4', 'G1' FROM demo_curve WHERE code = 'Sb2';

DROP TABLE temp.demo_vals; DROP TABLE temp.demo_temp; DROP TABLE temp.demo_curve; DROP TABLE temp.new_size;
COMMIT;
