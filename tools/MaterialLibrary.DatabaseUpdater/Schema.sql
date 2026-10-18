-- ==============================================================================
-- 1. TABELLE PRINCIPALI (PADRE E ESTENSIONI)
-- ==============================================================================

CREATE TABLE Main (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    Revision TEXT(30),
    NominalComposition TEXT,
    Specification TEXT(30) NOT NULL,
    Grade TEXT(30),
    ClassConditionTemper TEXT(30),
    AlloyDesignation TEXT(30),
    SMYS REAL,
    SMUS REAL,
    ElongationAtRuptureLong REAL,
    ElongationAtRuptureTransv REAL
);

CREATE UNIQUE INDEX UX_Main_FourFieldIdentity ON Main (
    Specification COLLATE NOCASE,
    COALESCE(Grade, '') COLLATE NOCASE,
    COALESCE(ClassConditionTemper, '') COLLATE NOCASE,
    COALESCE(AlloyDesignation, '') COLLATE NOCASE
);

-- Nuova tabella Extension (Relazione 1:1 con Main)
CREATE TABLE Extension (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_main INTEGER NOT NULL,
    ConstantPoissonRatio REAL,
    ConstantDensity REAL,
    FOREIGN KEY (id_main) REFERENCES Main(id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IDX_Extension_Main ON Extension (id_main);

CREATE TABLE Sizes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    SizeMin REAL,
    SizeMinIncluded BOOLEAN,
    SizeMax REAL,
    SizeMaxIncluded BOOLEAN
);

-- ==============================================================================
-- 2. TENSIONI AMMISSIBILI E PROPRIETÀ MECCANICHE (Sy, Su, S1, S2)
-- ==============================================================================

CREATE TABLE Material_Properties (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_main INTEGER NOT NULL,
    id_size INTEGER,
    PropertyCode TEXT NOT NULL,
    ProductForm TEXT,
    CHECK (PropertyCode IN ('S1','S2') OR ProductForm IS NULL),
    FOREIGN KEY (id_main) REFERENCES Main(id) ON DELETE CASCADE,
    FOREIGN KEY (id_size) REFERENCES Sizes(id) ON DELETE CASCADE
);
CREATE INDEX IDX_MaterialProperties_Main_Code ON Material_Properties (id_main, PropertyCode);

CREATE TABLE Property_Values (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_property INTEGER NOT NULL,
    Temperature REAL NOT NULL,
    StressValue REAL NOT NULL,
    FOREIGN KEY (id_property) REFERENCES Material_Properties(id) ON DELETE CASCADE
);
CREATE INDEX IDX_PropertyValues_Temp ON Property_Values (id_property, Temperature);

CREATE TABLE Property_Notes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_property INTEGER NOT NULL,
    note TEXT NOT NULL,
    note_type TEXT,
    FOREIGN KEY (id_property) REFERENCES Material_Properties(id) ON DELETE CASCADE
);

-- ==============================================================================
-- 3. PROPRIETÀ FISICHE TERMODIPENDENTI (E, Alpha, TC, TD, ecc.)
-- ==============================================================================

CREATE TABLE Physical_Properties (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_main INTEGER NOT NULL,
    PropertyCode TEXT NOT NULL,
    FOREIGN KEY (id_main) REFERENCES Main(id) ON DELETE CASCADE
);
CREATE INDEX IDX_PhysicalProperties_Main_Code ON Physical_Properties (id_main, PropertyCode);

CREATE TABLE Physical_Property_Values (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_physical_property INTEGER NOT NULL,
    Temperature REAL NOT NULL,
    PropertyValue REAL NOT NULL,
    FOREIGN KEY (id_physical_property) REFERENCES Physical_Properties(id) ON DELETE CASCADE
);
CREATE INDEX IDX_PhysicalValues_Temp ON Physical_Property_Values (id_physical_property, Temperature);

CREATE VIEW VW_ThermalCapacity_Calculated AS
SELECT
    m.id AS id_main,
    val_tc.Temperature,
    val_tc.PropertyValue AS TC_Value,
    val_td.PropertyValue AS TD_Value,
    val_rho.PropertyValue AS Density_Value,
    (val_tc.PropertyValue / (val_rho.PropertyValue * val_td.PropertyValue)) AS SpecificHeat_Calculated
FROM Main m
JOIN Physical_Properties prop_tc ON m.id = prop_tc.id_main AND prop_tc.PropertyCode = 'TC'
JOIN Physical_Property_Values val_tc ON prop_tc.id = val_tc.id_physical_property
JOIN Physical_Properties prop_td ON m.id = prop_td.id_main AND prop_td.PropertyCode = 'TD'
JOIN Physical_Property_Values val_td ON prop_td.id = val_td.id_physical_property AND val_td.Temperature = val_tc.Temperature
JOIN Physical_Properties prop_rho ON m.id = prop_rho.id_main AND prop_rho.PropertyCode = 'Density'
JOIN Physical_Property_Values val_rho ON prop_rho.id = val_rho.id_physical_property AND val_rho.Temperature = val_tc.Temperature;

-- ==============================================================================
-- 4. DATI NORMATIVI ASME
-- ==============================================================================

CREATE TABLE ASME_Data (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_main INTEGER NOT NULL,
    P_Number TEXT,
    Group_Number TEXT,
    Time_Dependent_Start_Temp REAL,
    Max_Temp_Sect_I REAL,
    Max_Temp_Sect_III REAL,
    Max_Temp_Sect_VIII_1 REAL,
    Max_Temp_Sect_VIII_2 REAL,
    FOREIGN KEY (id_main) REFERENCES Main(id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IDX_ASMEData_Main ON ASME_Data (id_main);

-- ==============================================================================
-- 5. CURVE STRESS-STRAIN E DATI DI CREEP / ROTTURA
-- ==============================================================================

CREATE TABLE Stress_Strain_Curves (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_main INTEGER NOT NULL,
    Temperature REAL NOT NULL,
    Time_Hours REAL,
    Curve_Type TEXT NOT NULL,
    FOREIGN KEY (id_main) REFERENCES Main(id) ON DELETE CASCADE,
    CHECK (
        (Curve_Type = 'Isochronous' AND Time_Hours IS NOT NULL) OR
        (Curve_Type != 'Isochronous')
    )
);
CREATE INDEX IDX_StressStrain_Main_TempTime ON Stress_Strain_Curves (id_main, Temperature, Time_Hours, Curve_Type);

CREATE TABLE Stress_Strain_Points (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_curve INTEGER NOT NULL,
    Strain REAL NOT NULL,
    Stress REAL NOT NULL,
    FOREIGN KEY (id_curve) REFERENCES Stress_Strain_Curves(id) ON DELETE CASCADE
);
CREATE INDEX IDX_StressStrain_Points_Strain ON Stress_Strain_Points (id_curve, Strain);

CREATE TABLE Creep_Rupture_Data (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_main INTEGER NOT NULL,
    PropertyCode TEXT NOT NULL,
    Temperature REAL NOT NULL,
    Duration_Hours REAL,
    StressValue REAL NOT NULL,
    FOREIGN KEY (id_main) REFERENCES Main(id) ON DELETE CASCADE
);
CREATE INDEX IDX_CreepRupture_Lookup ON Creep_Rupture_Data (id_main, PropertyCode, Temperature, Duration_Hours);

-- ==============================================================================
-- 6. MODELLI AVANZATI: LARSON-MILLER, CYCLIC, E METODO OMEGA
-- ==============================================================================

CREATE TABLE Larson_Miller_Parameters (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_main INTEGER NOT NULL,
    LM_Constant REAL NOT NULL,
    Reference_Source TEXT,
    FOREIGN KEY (id_main) REFERENCES Main(id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IDX_LarsonMiller_Main ON Larson_Miller_Parameters (id_main);

CREATE TABLE Larson_Miller_Curve_Points (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_lm_param INTEGER NOT NULL,
    StressValue REAL NOT NULL,
    LMP_Value REAL NOT NULL,
    FOREIGN KEY (id_lm_param) REFERENCES Larson_Miller_Parameters(id) ON DELETE CASCADE
);
CREATE INDEX IDX_LarsonMiller_Points_LMP ON Larson_Miller_Curve_Points (id_lm_param, LMP_Value);

CREATE TABLE Stress_Strain_Model_Parameters (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_main INTEGER NOT NULL,
    Temperature_Limit_C REAL,
    Temperature_Limit_F REAL,
    m2_Formula TEXT,
    Epsilon_p REAL,
    FOREIGN KEY (id_main) REFERENCES Main(id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IDX_StressStrainModel_Main ON Stress_Strain_Model_Parameters (id_main);

CREATE TABLE Cyclic_Curve_Header (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_main INTEGER NOT NULL,
    Component_Type TEXT,
    Thickness_Reference TEXT,
    FOREIGN KEY (id_main) REFERENCES Main(id) ON DELETE CASCADE
);

CREATE TABLE Cyclic_Curve_Values (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_cyclic_header INTEGER NOT NULL,
    Temperature REAL NOT NULL,
    n_css REAL NOT NULL,
    K_css REAL NOT NULL,
    FOREIGN KEY (id_cyclic_header) REFERENCES Cyclic_Curve_Header(id) ON DELETE CASCADE
);
CREATE INDEX IDX_CyclicValues_Temp ON Cyclic_Curve_Values (id_cyclic_header, Temperature);

CREATE TABLE Omega_Method_Parameters (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_main INTEGER NOT NULL,
    Material_Class TEXT,
    A0 REAL NOT NULL, A1 REAL NOT NULL, A2 REAL NOT NULL, A3 REAL NOT NULL, A4 REAL NOT NULL,
    B0 REAL NOT NULL, B1 REAL NOT NULL, B2 REAL NOT NULL, B3 REAL NOT NULL, B4 REAL NOT NULL,
    FOREIGN KEY (id_main) REFERENCES Main(id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IDX_OmegaMethod_Main ON Omega_Method_Parameters (id_main);

-- ==============================================================================
-- 7. EXTERNAL PRESSURE CHARTS
-- ==============================================================================

CREATE TABLE External_Pressure_Charts (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    Chart_Name TEXT NOT NULL UNIQUE
);

CREATE TABLE Material_EP_Charts (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_main INTEGER NOT NULL,
    id_ep_chart INTEGER NOT NULL,
    FOREIGN KEY (id_main) REFERENCES Main(id) ON DELETE CASCADE,
    FOREIGN KEY (id_ep_chart) REFERENCES External_Pressure_Charts(id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IDX_MaterialEPChart_Main ON Material_EP_Charts (id_main);

CREATE TABLE EP_Chart_Curves (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_ep_chart INTEGER NOT NULL,
    Temperature REAL NOT NULL,
    Elastic_Modulus REAL,
    FOREIGN KEY (id_ep_chart) REFERENCES External_Pressure_Charts(id) ON DELETE CASCADE
);
CREATE INDEX IDX_EPChartCurves_Chart_Temp ON EP_Chart_Curves (id_ep_chart, Temperature);

CREATE TABLE EP_Chart_Points (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    id_ep_curve INTEGER NOT NULL,
    Factor_A REAL NOT NULL,
    Factor_B REAL NOT NULL,
    FOREIGN KEY (id_ep_curve) REFERENCES EP_Chart_Curves(id) ON DELETE CASCADE
);
CREATE INDEX IDX_EPChartPoints_Curve_A ON EP_Chart_Points (id_ep_curve, Factor_A);
