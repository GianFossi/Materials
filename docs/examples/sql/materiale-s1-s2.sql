PRAGMA foreign_keys = ON;
BEGIN;

-- 1. Identità a quattro campi (Main + Material_Identity) con i minimi scalari a temperatura ambiente.
INSERT INTO Main (Specification, Grade, ClassConditionTemper, AlloyDesignation, NominalComposition, SMYS, SMUS)
VALUES ('SA-DEMO2', 'GR1', 'N', 'K88888', 'Carbon steel (demo)', 260, 485);
INSERT INTO Material_Identity (id_main, NaturalKey)
VALUES (last_insert_rowid(), '["SA-DEMO2","GR1","N","K88888"]');

-- 2. Intervalli di spessore t (mm). Ogni intervallo è una riga di Sizes; più curve possono condividerla.
CREATE TEMP TABLE new_size (label TEXT PRIMARY KEY, id INTEGER NOT NULL);
INSERT INTO Sizes (SizeMin, SizeMinIncluded, SizeMax, SizeMaxIncluded) VALUES (NULL, 0, 40.0, 1);    -- t <= 40
INSERT INTO new_size VALUES ('le40', last_insert_rowid());
INSERT INTO Sizes (SizeMin, SizeMinIncluded, SizeMax, SizeMaxIncluded) VALUES (40.0, 0, 100.0, 1);   -- 40 < t <= 100
INSERT INTO new_size VALUES ('40_100', last_insert_rowid());
INSERT INTO Sizes (SizeMin, SizeMinIncluded, SizeMax, SizeMaxIncluded) VALUES (NULL, 0, 100.0, 1);   -- t <= 100
INSERT INTO new_size VALUES ('le100', last_insert_rowid());
INSERT INTO Sizes (SizeMin, SizeMinIncluded, SizeMax, SizeMaxIncluded) VALUES (100.0, 0, 200.0, 1);  -- 100 < t <= 200
INSERT INTO new_size VALUES ('100_200', last_insert_rowid());
INSERT INTO Sizes (SizeMin, SizeMinIncluded, SizeMax, SizeMaxIncluded) VALUES (NULL, 0, 200.0, 1);   -- t <= 200
INSERT INTO new_size VALUES ('le200', last_insert_rowid());
-- Nessun limite dimensionale: la riga di Sizes con SizeMin e SizeMax entrambi NULL = QUALSIASI dimensione.
-- Esiste già nel riferimento (è condivisa da migliaia di curve): si riusa, non si duplica.
INSERT INTO Sizes (SizeMin, SizeMinIncluded, SizeMax, SizeMaxIncluded)
SELECT NULL, 0, NULL, 0 WHERE NOT EXISTS (SELECT 1 FROM Sizes WHERE SizeMin IS NULL AND SizeMax IS NULL);
INSERT INTO new_size VALUES ('any', (SELECT MIN(id) FROM Sizes WHERE SizeMin IS NULL AND SizeMax IS NULL));

-- 3. Elenco delle curve da creare: chiave, famiglia, forma di prodotto (solo S1/S2), intervallo, livello S1, tabella ASME di riferimento.
CREATE TEMP TABLE demo_curve (key TEXT PRIMARY KEY, code TEXT NOT NULL, form TEXT, size_label TEXT NOT NULL, level TEXT, src TEXT NOT NULL, id INTEGER);
INSERT INTO demo_curve (key, code, form, size_label, level, src) VALUES
    ('sy_le40',   'Sy', NULL, 'le40',    NULL, 'Sy'), ('sy_40_100',  'Sy', NULL, '40_100',  NULL, 'Sy'), ('sy_100_200',  'Sy', NULL, '100_200', NULL, 'Sy'),
    ('su_le40',   'Su', NULL, 'le40',    NULL, 'Su'), ('su_40_100',  'Su', NULL, '40_100',  NULL, 'Su'), ('su_100_200',  'Su', NULL, '100_200', NULL, 'Su'),
    ('s1_pl_le40',  'S1', 'PLATE',    'le40',    'Normal', '1A'), ('s1_plh_le40', 'S1', 'PLATE',    'le40',    'High', '1A'),
    ('s1_pl_40_100','S1', 'PLATE',    '40_100',  'Normal', '1A'), ('s1_fo_le100', 'S1', 'FORGINGS', 'le100',   'Normal', '1A'),
    ('s1_fo_100_200','S1','FORGINGS', '100_200', 'Normal', '1A'),
    ('s2_pl_le40',  'S2', 'PLATE',    'le40',    NULL, '5A'), ('s2_pl_40_100', 'S2', 'PLATE',    '40_100',  NULL, '5A'),
    ('s2_fo_le200', 'S2', 'FORGINGS', 'le200',   NULL, '5A'),
    ('s2_bar_any',  'S2', 'BAR',      'any',     NULL, '5A');    -- nessun limite dimensionale

INSERT INTO Material_Properties (id_main, id_size, PropertyCode, ProductForm)
SELECT m.id, z.id, c.code, c.form
FROM demo_curve c
JOIN new_size z ON z.label = c.size_label
JOIN Main m ON m.Specification = 'SA-DEMO2' AND m.Grade = 'GR1'
ORDER BY c.rowid;                                   -- gli id nascono consecutivi nell'ordine dell'elenco

-- Collega ogni voce dell'elenco all'id della curva appena creata.
UPDATE demo_curve
SET id = (SELECT MIN(p.id) FROM Material_Properties p JOIN Main m ON m.id = p.id_main
          WHERE m.Specification = 'SA-DEMO2' AND m.Grade = 'GR1') + rowid - 1;

-- 4. Punti: una riga per curva con i valori alle 10 temperature, poi "despivotata" in Property_Values (T in °C, valori in MPa).
CREATE TEMP TABLE demo_temp (T REAL PRIMARY KEY);
INSERT INTO demo_temp VALUES (40), (100), (150), (200), (250), (300), (350), (400), (450), (500);
CREATE TEMP TABLE demo_vals (key TEXT PRIMARY KEY, v40, v100, v150, v200, v250, v300, v350, v400, v450, v500);
INSERT INTO demo_vals VALUES
    ('sy_le40',   260, 240, 225, 210, 195, 180, 165, 150, 135, 120), ('sy_40_100',  250, 230, 215, 200, 185, 170, 155, 140, 125, 110),
    ('sy_100_200',240, 220, 205, 190, 175, 160, 145, 130, 115, 100),
    ('su_le40',   485, 485, 480, 475, 470, 460, 440, 410, 370, 320), ('su_40_100',  450, 450, 445, 440, 435, 425, 405, 380, 340, 290),
    ('su_100_200',430, 430, 425, 420, 415, 405, 385, 360, 320, 270),
    ('s1_pl_le40',  138, 138, 138, 138, 138, 136, 128, 101, 67, 34), ('s1_plh_le40', 160, 160, 160, 160, 158, 152, 140, 110, 70, 35),
    ('s1_pl_40_100',129, 129, 129, 129, 129, 127, 120,  95, 63, 32), ('s1_fo_le100', 138, 138, 138, 138, 138, 135, 126, 100, 66, 33),
    ('s1_fo_100_200',123,123, 123, 123, 123, 121, 114,  90, 60, 30),
    ('s2_pl_le40',  184, 184, 184, 184, 184, 183, 170, 134, 89, 45), ('s2_pl_40_100', 172, 172, 172, 172, 172, 170, 160, 127, 84, 43),
    ('s2_fo_le200', 164, 164, 164, 164, 164, 162, 152, 120, 80, 40), ('s2_bar_any', 150, 150, 150, 150, 150, 148, 140, 110, 73, 37);
INSERT INTO Property_Values (id_property, Temperature, StressValue)
SELECT c.id, t.T,
       CASE t.T WHEN 40 THEN v.v40 WHEN 100 THEN v.v100 WHEN 150 THEN v.v150 WHEN 200 THEN v.v200 WHEN 250 THEN v.v250
                WHEN 300 THEN v.v300 WHEN 350 THEN v.v350 WHEN 400 THEN v.v400 WHEN 450 THEN v.v450 ELSE v.v500 END
FROM demo_curve c JOIN demo_vals v ON v.key = c.key CROSS JOIN demo_temp t;

-- 5. Fonte: senza Property_Source le viste S1/S2 non mostrano la curva e le note generali della tabella non si applicano.
--    SourceTable indica la tabella ASME (1A = S1, 5A = S2); Fingerprint 'User' segnala che la riga non viene dall'importazione.
INSERT INTO Property_Source (id_property, SourceTable, File, CsvLine, Page, SizeAxis, MinYieldMPa, MinTensileMPa, RawFieldsJson, Fingerprint)
SELECT id, src, 'User/demo-' || key, 1, 'demo', 'thickness-or-unspecified',
       CASE WHEN code = 'Su' THEN NULL ELSE 260 END, 485, '{}', 'User'
FROM demo_curve;

-- 6. Metadati dell'editor: livello S1 (Normal/High), limiti per Section della curva e una nota utente per i forgiati.
INSERT INTO Property_Curve_Metadata (Code, CurveId, SizeAxis, S1StressLevel, CodeLimitsJson, UserNotes)
SELECT code, id, 'thickness-or-unspecified', level,
       CASE code
            WHEN 'S1' THEN '{"PNumber":"1","GroupNumber":"1","ExternalPressureChart":"CS-2","MaxTempSectionI":"454","MaxTempSectionIII":"371","MaxTempSectionVIII1":"450","MaxTempSectionVIII2":"371","MaxTempSectionXII":"343"}'
            WHEN 'S2' THEN '{"PNumber":"1","GroupNumber":"1","ExternalPressureChart":"CS-2","MaxTempSectionI":"","MaxTempSectionIII":"371","MaxTempSectionVIII1":"","MaxTempSectionVIII2":"400","MaxTempSectionXII":"343"}'
       END,
       CASE WHEN form = 'FORGINGS' THEN 'Forgiati: certificato 3.1 richiesto (nota utente di esempio)' END
FROM demo_curve;

-- 7. Note ufficiali ASME specifiche (le note generali della tabella arrivano da sole tramite Property_Source).
--    1A/G10 grafitizzazione sopra 425 °C, 1A/T1 valori time-dependent da 370 °C, 1A/S1 Section I; 1A/G5 solo per la variante High.
INSERT INTO Property_Note_Links (id_property, SourceTable, Code)
SELECT c.id, '1A', n.code
FROM demo_curve c CROSS JOIN (SELECT 'G10' AS code UNION ALL SELECT 'T1' UNION ALL SELECT 'S1') n
WHERE c.code = 'S1';
INSERT INTO Property_Note_Links (id_property, SourceTable, Code)
SELECT id, '1A', 'G5' FROM demo_curve WHERE level = 'High';

DROP TABLE temp.demo_vals; DROP TABLE temp.demo_temp; DROP TABLE temp.demo_curve; DROP TABLE temp.new_size;
COMMIT;
