PRAGMA foreign_keys = ON;
BEGIN;

-- Proprietà fisiche (valori dimostrativi) per entrambi i materiali di esempio.
-- Codici di storage: E, Alpha, AlphaInstantaneous, Cp (= c), TC (= k), TD (= a).
CREATE TEMP TABLE demo_family (code TEXT PRIMARY KEY);
INSERT INTO demo_family VALUES ('E'), ('Alpha'), ('AlphaInstantaneous'), ('Cp'), ('TC'), ('TD');

INSERT INTO Physical_Properties (id_main, PropertyCode)
SELECT m.id, f.code FROM Main m CROSS JOIN demo_family f WHERE m.Specification IN ('SA-DEMO2', 'SA-DEMO3');

-- Punti T (°C) / valore: E in MPa, Alpha e AlphaInstantaneous in 1/°C (Alpha medio riferito a 20 °C), Cp in J/(kg*K), TC in W/(m*K), TD in m2/s.
CREATE TEMP TABLE demo_phys (code TEXT, T REAL, V REAL);
INSERT INTO demo_phys VALUES
    ('E', 20, 202000), ('E', 100, 198000), ('E', 200, 193000), ('E', 300, 186000), ('E', 400, 179000), ('E', 500, 170000),
    ('Alpha', 100, 12.0E-06), ('Alpha', 200, 12.6E-06), ('Alpha', 300, 13.1E-06), ('Alpha', 400, 13.5E-06), ('Alpha', 500, 13.9E-06),
    ('AlphaInstantaneous', 100, 12.4E-06), ('AlphaInstantaneous', 200, 13.0E-06), ('AlphaInstantaneous', 300, 13.5E-06), ('AlphaInstantaneous', 400, 13.9E-06), ('AlphaInstantaneous', 500, 14.3E-06),
    ('Cp', 20, 470), ('Cp', 100, 487), ('Cp', 200, 512), ('Cp', 300, 537), ('Cp', 400, 565), ('Cp', 500, 600),
    ('TC', 20, 51.9), ('TC', 100, 51.1), ('TC', 200, 49.0), ('TC', 300, 46.2), ('TC', 400, 43.0), ('TC', 500, 39.6),
    ('TD', 20, 14.0E-06), ('TD', 100, 13.4E-06), ('TD', 200, 12.4E-06), ('TD', 300, 11.4E-06), ('TD', 400, 10.3E-06), ('TD', 500, 9.0E-06);
INSERT INTO Physical_Property_Values (id_physical_property, Temperature, PropertyValue)
SELECT pp.id, d.T, d.V FROM Physical_Properties pp JOIN Main m ON m.id = pp.id_main JOIN demo_phys d ON d.code = pp.PropertyCode
WHERE m.Specification IN ('SA-DEMO2', 'SA-DEMO3');

-- Costanti: densità (kg/m3) e Poisson.
INSERT INTO Extension (id_main, ConstantDensity, ConstantPoissonRatio)
SELECT id, 7850, 0.30 FROM Main WHERE Specification IN ('SA-DEMO2', 'SA-DEMO3');

-- Diagramma di pressione esterna per la lamiera (A adimensionale, B in MPa; nel tratto elastico B = 2*A*E/3).
INSERT INTO External_Pressure_Charts (Chart_Name) VALUES ('DEMO-CS-1');
INSERT INTO EP_Chart_Curves (id_ep_chart, Temperature, Elastic_Modulus) SELECT id, 100, 198000 FROM External_Pressure_Charts WHERE Chart_Name = 'DEMO-CS-1';
INSERT INTO EP_Chart_Curves (id_ep_chart, Temperature, Elastic_Modulus) SELECT id, 300, 186000 FROM External_Pressure_Charts WHERE Chart_Name = 'DEMO-CS-1';
INSERT INTO EP_Chart_Points (id_ep_curve, Factor_A, Factor_B)
SELECT cu.id, p.A, p.B
FROM EP_Chart_Curves cu
JOIN External_Pressure_Charts ch ON ch.id = cu.id_ep_chart AND ch.Chart_Name = 'DEMO-CS-1'
JOIN (SELECT 100 AS T, 1.0E-05 AS A, 1.32 AS B UNION ALL SELECT 100, 1.0E-04, 13.2 UNION ALL SELECT 100, 1.0E-03, 90.0 UNION ALL SELECT 100, 2.0E-03, 105.0
      UNION ALL SELECT 300, 1.0E-05, 1.24 UNION ALL SELECT 300, 1.0E-04, 12.4 UNION ALL SELECT 300, 1.0E-03, 70.0 UNION ALL SELECT 300, 2.0E-03, 85.0) p
  ON p.T = cu.Temperature;
INSERT INTO Material_EP_Charts (id_main, id_ep_chart)
SELECT m.id, ch.id FROM Main m, External_Pressure_Charts ch WHERE m.Specification = 'SA-DEMO2' AND ch.Chart_Name = 'DEMO-CS-1';

DROP TABLE temp.demo_phys; DROP TABLE temp.demo_family;
COMMIT;
