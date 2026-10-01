-- ============================================================
-- Beacon — Demo Seed Script
-- ============================================================
-- Populates the database with edge-case demo data:
--
--   Core (12-month history):
--     3 banks × 12 monthly statements × various transactions
--     2 salary profiles × 12 slips each, with line items
--     10 categories + 33 auto-categorisation rules
--     8 grocery categories + 15 grocery category rules
--     24 grocery receipts (Continente + Pingo Doce, 12 months each)
--     108 grocery items
--
--   Edge-case additions:
--     MBcp bank — 3 statements including transactions >= €10 000 and
--       <= -€10 000; statement 39 has a deliberate balance mismatch
--       to trigger the parse-warning UI banner
--     Tech Lead salary profile — 6 slips with gross up to €8 500,
--       holiday bonus line item, and one slip where income items ≠ gross
--       to trigger the salary parse-warning banner
--     Mercadona grocery store — 3 receipts; one item at €150 (large
--       unit price) and one at qty 12 (high quantity); same category
--       IDs as other stores to exercise UI category deduplication
--     5 excluded transactions (mix of categorised and uncategorised)
--     3 excluded grocery items
--
--   Features with no other demo data (sections 26–32):
--     Trade Republic — 12 statements with monthly "Savings plan execution"
--       ETF buys (excluded, mirrored as investment lots) and Deel payouts
--     MEAL CARD — 12 statements as the meal card text import stores them
--     Investments — the savings-plan ETF and gold in grams (three buys,
--       one sell), with monthly price snapshots and a week of daily ones
--     micro1 profile — 3 EUR slips with a "Deel exchange fee" deduction;
--       the March one merges a second pay run
--     Grocery receipt category mappings, and items filed by them
--     Names with an accented first letter ("Ótica", "Óleos & Condimentos",
--       "Échelle Labs (micro1)") for the DISPLAY_ORDER sort
--
-- The dates here run from April 2025 to March 2026. scripts/seed-demo.ps1 moves every date by
-- whole months so that March 2026 becomes the current month, keeping month ends on month ends,
-- so the dashboard and the current month always have data.
--
-- SQLite. Usage: scripts/seed-demo.ps1, which runs it through scripts/SeedRunner. SeedRunner rewrites the decimals afterwards in the
-- text form EF Core uses, so amounts may be written here as plain numbers.
--
-- WARNING: Will fail on duplicate key if data already exists.
-- Run against an empty or freshly migrated database only.
-- ============================================================

BEGIN TRANSACTION;

-- ============================================================
-- 1. CATEGORIES
-- ============================================================
-- Seeding categories...

INSERT INTO [Categories] ([Id],[Name],[Color],[IsProtected]) VALUES
( 1,'Housing',       '#ef4444',0),
( 2,'Food & Dining', '#f97316',0),
( 3,'Transport',     '#eab308',0),
( 4,'Utilities',     '#22c55e',0),
( 5,'Health',        '#06b6d4',0),
( 6,'Entertainment', '#8b5cf6',0),
( 7,'Shopping',      '#ec4899',0),
( 8,'Salary',        '#10b981',1),
( 9,'Transfers',     '#64748b',1),
(10,'Other',         '#94a3b8',0);


-- ============================================================
-- 2. CATEGORY RULES
-- ============================================================
-- Seeding category rules...

INSERT INTO [CategoryRules] ([Id],[CategoryId],[Pattern],[Value]) VALUES
-- Housing
( 1,1,'RENDA',NULL),
( 2,1,'IMOBILIARIA',NULL),
-- Food & Dining
( 3,2,'CONTINENTE',NULL),
( 4,2,'PINGO DOCE',NULL),
( 5,2,'LIDL',NULL),
( 6,2,'UBER EATS',NULL),
( 7,2,'GLOVO',NULL),
( 8,2,'RESTAURANTE',NULL),
-- Transport
( 9,3,'METRO',NULL),
(10,3,'UBER',NULL),
(11,3,'BOLT',NULL),
(12,3,'GALP',NULL),
(13,3,'CP ',NULL),
(14,3,'CARRIS',NULL),
-- Utilities
(15,4,'EDP',NULL),
(16,4,'MEO',NULL),
(17,4,'AGUAS',NULL),
(18,4,'VODAFONE',NULL),
-- Health
(19,5,'FARMACIA',NULL),
(20,5,'CLINICA',NULL),
(21,5,'SNS',NULL),
-- Entertainment
(22,6,'NETFLIX',NULL),
(23,6,'SPOTIFY',NULL),
(24,6,'CINEMA',NULL),
(25,6,'STEAM',NULL),
-- Shopping
(26,7,'AMAZON',NULL),
(27,7,'ZARA',NULL),
(28,7,'PRIMARK',NULL),
(29,7,'WORTEN',NULL),
-- Salary
(30,8,'SALARIO',NULL),
(31,8,'VENCIMENTO',NULL),
-- Transfers
(32,9,'TRANSFERENCIA',NULL),
(33,9,'TRF ',NULL);


-- ============================================================
-- 3. MONTHLY STATEMENTS  (3 banks × 12 months = 36)
-- ============================================================
-- Seeding monthly statements...

INSERT INTO [MonthlyStatements]
  ([Id],[Bank],[Account],[PeriodFrom],[PeriodTo],[Currency],[OpeningBalance],[ClosingBalance],[SourceFile],[PdfPath],[FileHash],[ImportedAt])
VALUES
-- ActivoBank (1–12)
( 1,'ActivoBank','PT50 0023 0000 1234 5678 9','2025-04-01','2025-04-30','EUR', 5200.00,  5384.36,'activo_2025_04.pdf',NULL,NULL,'2025-05-03 10:00:00'),
( 2,'ActivoBank','PT50 0023 0000 1234 5678 9','2025-05-01','2025-05-31','EUR', 5384.36,  5571.37,'activo_2025_05.pdf',NULL,NULL,'2025-06-02 10:00:00'),
( 3,'ActivoBank','PT50 0023 0000 1234 5678 9','2025-06-01','2025-06-30','EUR', 5571.37,  5756.88,'activo_2025_06.pdf',NULL,NULL,'2025-07-02 10:00:00'),
( 4,'ActivoBank','PT50 0023 0000 1234 5678 9','2025-07-01','2025-07-31','EUR', 5756.88,  7316.31,'activo_2025_07.pdf',NULL,NULL,'2025-08-02 10:00:00'),
( 5,'ActivoBank','PT50 0023 0000 1234 5678 9','2025-08-01','2025-08-31','EUR', 7316.31,  7495.67,'activo_2025_08.pdf',NULL,NULL,'2025-09-03 10:00:00'),
( 6,'ActivoBank','PT50 0023 0000 1234 5678 9','2025-09-01','2025-09-30','EUR', 7495.67,  7684.03,'activo_2025_09.pdf',NULL,NULL,'2025-10-02 10:00:00'),
( 7,'ActivoBank','PT50 0023 0000 1234 5678 9','2025-10-01','2025-10-31','EUR', 7684.03,  7846.69,'activo_2025_10.pdf',NULL,NULL,'2025-11-03 10:00:00'),
( 8,'ActivoBank','PT50 0023 0000 1234 5678 9','2025-11-01','2025-11-30','EUR', 7846.69,  8023.65,'activo_2025_11.pdf',NULL,NULL,'2025-12-02 10:00:00'),
( 9,'ActivoBank','PT50 0023 0000 1234 5678 9','2025-12-01','2025-12-31','EUR', 8023.65,  9554.86,'activo_2025_12.pdf',NULL,NULL,'2026-01-03 10:00:00'),
(10,'ActivoBank','PT50 0023 0000 1234 5678 9','2026-01-01','2026-01-31','EUR', 9554.86,  9731.07,'activo_2026_01.pdf',NULL,NULL,'2026-02-02 10:00:00'),
(11,'ActivoBank','PT50 0023 0000 1234 5678 9','2026-02-01','2026-02-28','EUR', 9731.07,  9974.93,'activo_2026_02.pdf',NULL,NULL,'2026-03-03 10:00:00'),
(12,'ActivoBank','PT50 0023 0000 1234 5678 9','2026-03-01','2026-03-31','EUR', 9974.93, 10198.24,'activo_2026_03.pdf',NULL,NULL,'2026-04-02 10:00:00'),
-- BPI (13–24)
(13,'BPI','PT50 0010 0000 5678 9012 1','2025-04-01','2025-04-30','EUR',2100.00,2262.62,'bpi_2025_04.pdf',NULL,NULL,'2025-05-04 10:00:00'),
(14,'BPI','PT50 0010 0000 5678 9012 1','2025-05-01','2025-05-31','EUR',2262.62,2460.69,'bpi_2025_05.pdf',NULL,NULL,'2025-06-03 10:00:00'),
(15,'BPI','PT50 0010 0000 5678 9012 1','2025-06-01','2025-06-30','EUR',2460.69,2614.40,'bpi_2025_06.pdf',NULL,NULL,'2025-07-03 10:00:00'),
(16,'BPI','PT50 0010 0000 5678 9012 1','2025-07-01','2025-07-31','EUR',2614.40,2735.26,'bpi_2025_07.pdf',NULL,NULL,'2025-08-03 10:00:00'),
(17,'BPI','PT50 0010 0000 5678 9012 1','2025-08-01','2025-08-31','EUR',2735.26,2942.97,'bpi_2025_08.pdf',NULL,NULL,'2025-09-04 10:00:00'),
(18,'BPI','PT50 0010 0000 5678 9012 1','2025-09-01','2025-09-30','EUR',2942.97,3099.83,'bpi_2025_09.pdf',NULL,NULL,'2025-10-03 10:00:00'),
(19,'BPI','PT50 0010 0000 5678 9012 1','2025-10-01','2025-10-31','EUR',3099.83,3213.39,'bpi_2025_10.pdf',NULL,NULL,'2025-11-04 10:00:00'),
(20,'BPI','PT50 0010 0000 5678 9012 1','2025-11-01','2025-11-30','EUR',3213.39,3413.00,'bpi_2025_11.pdf',NULL,NULL,'2025-12-03 10:00:00'),
(21,'BPI','PT50 0010 0000 5678 9012 1','2025-12-01','2025-12-31','EUR',3413.00,3409.41,'bpi_2025_12.pdf',NULL,NULL,'2026-01-04 10:00:00'),
(22,'BPI','PT50 0010 0000 5678 9012 1','2026-01-01','2026-01-31','EUR',3409.41,3672.02,'bpi_2026_01.pdf',NULL,NULL,'2026-02-03 10:00:00'),
(23,'BPI','PT50 0010 0000 5678 9012 1','2026-02-01','2026-02-28','EUR',3672.02,3909.63,'bpi_2026_02.pdf',NULL,NULL,'2026-03-04 10:00:00'),
(24,'BPI','PT50 0010 0000 5678 9012 1','2026-03-01','2026-03-31','EUR',3909.63,4115.45,'bpi_2026_03.pdf',NULL,NULL,'2026-04-03 10:00:00'),
-- Revolut (25–36)
(25,'Revolut','LT12 3250 0100 0123 4567','2025-04-01','2025-04-30','EUR', 850.00, 921.53,'revolut_2025_04.pdf',NULL,NULL,'2025-05-02 10:00:00'),
(26,'Revolut','LT12 3250 0100 0123 4567','2025-05-01','2025-05-31','EUR', 921.53, 979.26,'revolut_2025_05.pdf',NULL,NULL,'2025-06-01 10:00:00'),
(27,'Revolut','LT12 3250 0100 0123 4567','2025-06-01','2025-06-30','EUR', 979.26, 930.89,'revolut_2025_06.pdf',NULL,NULL,'2025-07-01 10:00:00'),
(28,'Revolut','LT12 3250 0100 0123 4567','2025-07-01','2025-07-31','EUR', 930.89, 932.92,'revolut_2025_07.pdf',NULL,NULL,'2025-08-01 10:00:00'),
(29,'Revolut','LT12 3250 0100 0123 4567','2025-08-01','2025-08-31','EUR', 932.92,1073.45,'revolut_2025_08.pdf',NULL,NULL,'2025-09-01 10:00:00'),
(30,'Revolut','LT12 3250 0100 0123 4567','2025-09-01','2025-09-30','EUR',1073.45,1161.08,'revolut_2025_09.pdf',NULL,NULL,'2025-10-01 10:00:00'),
(31,'Revolut','LT12 3250 0100 0123 4567','2025-10-01','2025-10-31','EUR',1161.08,1256.11,'revolut_2025_10.pdf',NULL,NULL,'2025-11-01 10:00:00'),
(32,'Revolut','LT12 3250 0100 0123 4567','2025-11-01','2025-11-30','EUR',1256.11,1326.14,'revolut_2025_11.pdf',NULL,NULL,'2025-12-01 10:00:00'),
(33,'Revolut','LT12 3250 0100 0123 4567','2025-12-01','2025-12-31','EUR',1326.14,1366.17,'revolut_2025_12.pdf',NULL,NULL,'2026-01-01 10:00:00'),
(34,'Revolut','LT12 3250 0100 0123 4567','2026-01-01','2026-01-31','EUR',1366.17,1448.80,'revolut_2026_01.pdf',NULL,NULL,'2026-02-01 10:00:00'),
(35,'Revolut','LT12 3250 0100 0123 4567','2026-02-01','2026-02-28','EUR',1448.80,1571.84,'revolut_2026_02.pdf',NULL,NULL,'2026-03-01 10:00:00'),
(36,'Revolut','LT12 3250 0100 0123 4567','2026-03-01','2026-03-31','EUR',1571.84,1678.37,'revolut_2026_03.pdf',NULL,NULL,'2026-04-01 10:00:00');


-- ============================================================
-- 4. TRANSACTIONS — ActivoBank (12 per month, IDs 1–144)
--    Pattern per month:
--      1 Rent | 2 Continente | 3 EDP | 4 MEO | 5 Aguas | 6 Metro
--      7 Salary | 8 Pingo Doce | 9 Galp | 10 Uber
--      11 Farmacia (odd months) or Bolt (even months)
--      12 Transfer to BPI [internal]
-- ============================================================
-- Seeding ActivoBank transactions...

INSERT INTO [Transactions]
  ([Id],[StatementId],[DatePosting],[DateValue],[Description],[Amount],[Type],[Balance],[CategoryId],[CategoryRuleId],[CategorySetManually],[IsExcluded])
VALUES
-- === April 2025 (Stmt 1) ===
(  1, 1,'2025-04-01','2025-04-01','RENDA APARTAMENTO LISBOA',              800.00,'debit', 4400.00,  1, 1,0,0),
(  2, 1,'2025-04-03','2025-04-03','CONTINENTE MODELO COLOMBO',              65.43,'debit', 4334.57,  2, 3,0,0),
(  3, 1,'2025-04-05','2025-04-07','EDP COMERCIAL SA',                       45.30,'debit', 4289.27,  4,15,0,0),
(  4, 1,'2025-04-07','2025-04-07','MEO COMUNICACOES SA',                    34.99,'debit', 4254.28,  4,16,0,0),
(  5, 1,'2025-04-09','2025-04-09','AGUAS DE LISBOA EM',                     12.50,'debit', 4241.78,  4,17,0,0),
(  6, 1,'2025-04-11','2025-04-11','METRO DE LISBOA EP',                     24.00,'debit', 4217.78,  3, 9,0,0),
(  7, 1,'2025-04-15','2025-04-15','SALARIO EMPRESA ABC LDA ABR/2025',     1800.00,'credit',6017.78,  8,30,0,0),
(  8, 1,'2025-04-17','2025-04-17','PINGO DOCE ALFAMA',                      48.72,'debit', 5969.06,  2, 4,0,0),
(  9, 1,'2025-04-19','2025-04-19','GALP COMBUSTIVEIS SA',                   55.00,'debit', 5914.06,  3,12,0,0),
( 10, 1,'2025-04-22','2025-04-22','UBER* TRIP',                             11.20,'debit', 5902.86,  3,10,0,0),
( 11, 1,'2025-04-25','2025-04-25','FARMACIA WELLS COLOMBO',                 18.50,'debit', 5884.36,  5,19,0,0),
( 12, 1,'2025-04-28','2025-04-28','TRANSFERENCIA PARA BPI 5678',           500.00,'debit', 5384.36,  9,32,0,1),
-- === May 2025 (Stmt 2) ===
( 13, 2,'2025-05-01','2025-05-01','RENDA APARTAMENTO LISBOA',              800.00,'debit', 4584.36,  1, 1,0,0),
( 14, 2,'2025-05-03','2025-05-03','CONTINENTE MODELO COLOMBO',              71.22,'debit', 4513.14,  2, 3,0,0),
( 15, 2,'2025-05-05','2025-05-07','EDP COMERCIAL SA',                       45.30,'debit', 4467.84,  4,15,0,0),
( 16, 2,'2025-05-07','2025-05-07','MEO COMUNICACOES SA',                    34.99,'debit', 4432.85,  4,16,0,0),
( 17, 2,'2025-05-09','2025-05-09','AGUAS DE LISBOA EM',                     12.50,'debit', 4420.35,  4,17,0,0),
( 18, 2,'2025-05-11','2025-05-11','METRO DE LISBOA EP',                     24.00,'debit', 4396.35,  3, 9,0,0),
( 19, 2,'2025-05-15','2025-05-15','SALARIO EMPRESA ABC LDA MAI/2025',     1800.00,'credit',6196.35,  8,30,0,0),
( 20, 2,'2025-05-17','2025-05-17','PINGO DOCE ALFAMA',                      52.18,'debit', 6144.17,  2, 4,0,0),
( 21, 2,'2025-05-19','2025-05-19','GALP COMBUSTIVEIS SA',                   50.00,'debit', 6094.17,  3,12,0,0),
( 22, 2,'2025-05-22','2025-05-22','UBER* TRIP',                              8.50,'debit', 6085.67,  3,10,0,0),
( 23, 2,'2025-05-25','2025-05-25','BOLT* RIDE',                             14.30,'debit', 6071.37,  3,11,0,0),
( 24, 2,'2025-05-28','2025-05-28','TRANSFERENCIA PARA BPI 5678',           500.00,'debit', 5571.37,  9,32,0,1),
-- === June 2025 (Stmt 3) ===
( 25, 3,'2025-06-01','2025-06-01','RENDA APARTAMENTO LISBOA',              800.00,'debit', 4771.37,  1, 1,0,0),
( 26, 3,'2025-06-03','2025-06-03','CONTINENTE MODELO COLOMBO',              68.90,'debit', 4702.47,  2, 3,0,0),
( 27, 3,'2025-06-05','2025-06-07','EDP COMERCIAL SA',                       45.30,'debit', 4657.17,  4,15,0,0),
( 28, 3,'2025-06-07','2025-06-07','MEO COMUNICACOES SA',                    34.99,'debit', 4622.18,  4,16,0,0),
( 29, 3,'2025-06-09','2025-06-09','AGUAS DE LISBOA EM',                     12.50,'debit', 4609.68,  4,17,0,0),
( 30, 3,'2025-06-11','2025-06-11','METRO DE LISBOA EP',                     24.00,'debit', 4585.68,  3, 9,0,0),
( 31, 3,'2025-06-15','2025-06-15','SALARIO EMPRESA ABC LDA JUN/2025',     1800.00,'credit',6385.68,  8,30,0,0),
( 32, 3,'2025-06-17','2025-06-17','PINGO DOCE ALFAMA',                      44.50,'debit', 6341.18,  2, 4,0,0),
( 33, 3,'2025-06-19','2025-06-19','GALP COMBUSTIVEIS SA',                   48.00,'debit', 6293.18,  3,12,0,0),
( 34, 3,'2025-06-22','2025-06-22','UBER* TRIP',                             14.30,'debit', 6278.88,  3,10,0,0),
( 35, 3,'2025-06-25','2025-06-25','FARMACIA WELLS COLOMBO',                 22.00,'debit', 6256.88,  5,19,0,0),
( 36, 3,'2025-06-28','2025-06-28','TRANSFERENCIA PARA BPI 5678',           500.00,'debit', 5756.88,  9,32,0,1),
-- === July 2025 (Stmt 4) — Vacation subsidy ===
( 37, 4,'2025-07-01','2025-07-01','RENDA APARTAMENTO LISBOA',              800.00,'debit', 4956.88,  1, 1,0,0),
( 38, 4,'2025-07-03','2025-07-03','CONTINENTE MODELO COLOMBO',              73.15,'debit', 4883.73,  2, 3,0,0),
( 39, 4,'2025-07-05','2025-07-07','EDP COMERCIAL SA',                       45.30,'debit', 4838.43,  4,15,0,0),
( 40, 4,'2025-07-07','2025-07-07','MEO COMUNICACOES SA',                    34.99,'debit', 4803.44,  4,16,0,0),
( 41, 4,'2025-07-09','2025-07-09','AGUAS DE LISBOA EM',                     12.50,'debit', 4790.94,  4,17,0,0),
( 42, 4,'2025-07-11','2025-07-11','METRO DE LISBOA EP',                     24.00,'debit', 4766.94,  3, 9,0,0),
( 43, 4,'2025-07-15','2025-07-15','SALARIO EMPRESA ABC LDA JUL/2025 INC. SUBSIDIO FERIAS',3200.00,'credit',7966.94,8,30,0,0),
( 44, 4,'2025-07-17','2025-07-17','PINGO DOCE ALFAMA',                      58.43,'debit', 7908.51,  2, 4,0,0),
( 45, 4,'2025-07-19','2025-07-19','GALP COMBUSTIVEIS SA',                   60.00,'debit', 7848.51,  3,12,0,0),
( 46, 4,'2025-07-22','2025-07-22','UBER* TRIP',                              9.80,'debit', 7838.71,  3,10,0,0),
( 47, 4,'2025-07-25','2025-07-25','BOLT* RIDE',                             22.40,'debit', 7816.31,  3,11,0,0),
( 48, 4,'2025-07-28','2025-07-28','TRANSFERENCIA PARA BPI 5678',           500.00,'debit', 7316.31,  9,32,0,1),
-- === August 2025 (Stmt 5) ===
( 49, 5,'2025-08-01','2025-08-01','RENDA APARTAMENTO LISBOA',              800.00,'debit', 6516.31,  1, 1,0,0),
( 50, 5,'2025-08-03','2025-08-03','CONTINENTE MODELO COLOMBO',              69.43,'debit', 6446.88,  2, 3,0,0),
( 51, 5,'2025-08-05','2025-08-07','EDP COMERCIAL SA',                       45.30,'debit', 6401.58,  4,15,0,0),
( 52, 5,'2025-08-07','2025-08-07','MEO COMUNICACOES SA',                    34.99,'debit', 6366.59,  4,16,0,0),
( 53, 5,'2025-08-09','2025-08-09','AGUAS DE LISBOA EM',                     12.50,'debit', 6354.09,  4,17,0,0),
( 54, 5,'2025-08-11','2025-08-11','METRO DE LISBOA EP',                     24.00,'debit', 6330.09,  3, 9,0,0),
( 55, 5,'2025-08-15','2025-08-15','SALARIO EMPRESA ABC LDA AGO/2025',     1800.00,'credit',8130.09,  8,30,0,0),
( 56, 5,'2025-08-17','2025-08-17','PINGO DOCE ALFAMA',                      51.22,'debit', 8078.87,  2, 4,0,0),
( 57, 5,'2025-08-19','2025-08-19','GALP COMBUSTIVEIS SA',                   55.00,'debit', 8023.87,  3,12,0,0),
( 58, 5,'2025-08-22','2025-08-22','UBER* TRIP',                             12.40,'debit', 8011.47,  3,10,0,0),
( 59, 5,'2025-08-25','2025-08-25','FARMACIA WELLS COLOMBO',                 15.80,'debit', 7995.67,  5,19,0,0),
( 60, 5,'2025-08-28','2025-08-28','TRANSFERENCIA PARA BPI 5678',           500.00,'debit', 7495.67,  9,32,0,1),
-- === September 2025 (Stmt 6) ===
( 61, 6,'2025-09-01','2025-09-01','RENDA APARTAMENTO LISBOA',              800.00,'debit', 6695.67,  1, 1,0,0),
( 62, 6,'2025-09-03','2025-09-03','CONTINENTE MODELO COLOMBO',              66.10,'debit', 6629.57,  2, 3,0,0),
( 63, 6,'2025-09-05','2025-09-07','EDP COMERCIAL SA',                       45.30,'debit', 6584.27,  4,15,0,0),
( 64, 6,'2025-09-07','2025-09-07','MEO COMUNICACOES SA',                    34.99,'debit', 6549.28,  4,16,0,0),
( 65, 6,'2025-09-09','2025-09-09','AGUAS DE LISBOA EM',                     12.50,'debit', 6536.78,  4,17,0,0),
( 66, 6,'2025-09-11','2025-09-11','METRO DE LISBOA EP',                     24.00,'debit', 6512.78,  3, 9,0,0),
( 67, 6,'2025-09-15','2025-09-15','SALARIO EMPRESA ABC LDA SET/2025',     1800.00,'credit',8312.78,  8,30,0,0),
( 68, 6,'2025-09-17','2025-09-17','PINGO DOCE ALFAMA',                      47.95,'debit', 8264.83,  2, 4,0,0),
( 69, 6,'2025-09-19','2025-09-19','GALP COMBUSTIVEIS SA',                   52.00,'debit', 8212.83,  3,12,0,0),
( 70, 6,'2025-09-22','2025-09-22','UBER* TRIP',                             10.60,'debit', 8202.23,  3,10,0,0),
( 71, 6,'2025-09-25','2025-09-25','BOLT* RIDE',                             18.20,'debit', 8184.03,  3,11,0,0),
( 72, 6,'2025-09-28','2025-09-28','TRANSFERENCIA PARA BPI 5678',           500.00,'debit', 7684.03,  9,32,0,1),
-- === October 2025 (Stmt 7) ===
( 73, 7,'2025-10-01','2025-10-01','RENDA APARTAMENTO LISBOA',              800.00,'debit', 6884.03,  1, 1,0,0),
( 74, 7,'2025-10-03','2025-10-03','CONTINENTE MODELO COLOMBO',              70.55,'debit', 6813.48,  2, 3,0,0),
( 75, 7,'2025-10-05','2025-10-07','EDP COMERCIAL SA',                       45.30,'debit', 6768.18,  4,15,0,0),
( 76, 7,'2025-10-07','2025-10-07','MEO COMUNICACOES SA',                    34.99,'debit', 6733.19,  4,16,0,0),
( 77, 7,'2025-10-09','2025-10-09','AGUAS DE LISBOA EM',                     12.50,'debit', 6720.69,  4,17,0,0),
( 78, 7,'2025-10-11','2025-10-11','METRO DE LISBOA EP',                     24.00,'debit', 6696.69,  3, 9,0,0),
( 79, 7,'2025-10-15','2025-10-15','SALARIO EMPRESA ABC LDA OUT/2025',     1800.00,'credit',8496.69,  8,30,0,0),
( 80, 7,'2025-10-17','2025-10-17','PINGO DOCE ALFAMA',                      53.80,'debit', 8442.89,  2, 4,0,0),
( 81, 7,'2025-10-19','2025-10-19','GALP COMBUSTIVEIS SA',                   58.00,'debit', 8384.89,  3,12,0,0),
( 82, 7,'2025-10-22','2025-10-22','UBER* TRIP',                             13.70,'debit', 8371.19,  3,10,0,0),
( 83, 7,'2025-10-25','2025-10-25','FARMACIA WELLS COLOMBO',                 24.50,'debit', 8346.69,  5,19,0,0),
( 84, 7,'2025-10-28','2025-10-28','TRANSFERENCIA PARA BPI 5678',           500.00,'debit', 7846.69,  9,32,0,1),
-- === November 2025 (Stmt 8) ===
( 85, 8,'2025-11-01','2025-11-01','RENDA APARTAMENTO LISBOA',              800.00,'debit', 7046.69,  1, 1,0,0),
( 86, 8,'2025-11-03','2025-11-03','CONTINENTE MODELO COLOMBO',              72.30,'debit', 6974.39,  2, 3,0,0),
( 87, 8,'2025-11-05','2025-11-07','EDP COMERCIAL SA',                       45.30,'debit', 6929.09,  4,15,0,0),
( 88, 8,'2025-11-07','2025-11-07','MEO COMUNICACOES SA',                    34.99,'debit', 6894.10,  4,16,0,0),
( 89, 8,'2025-11-09','2025-11-09','AGUAS DE LISBOA EM',                     12.50,'debit', 6881.60,  4,17,0,0),
( 90, 8,'2025-11-11','2025-11-11','METRO DE LISBOA EP',                     24.00,'debit', 6857.60,  3, 9,0,0),
( 91, 8,'2025-11-15','2025-11-15','SALARIO EMPRESA ABC LDA NOV/2025',     1800.00,'credit',8657.60,  8,30,0,0),
( 92, 8,'2025-11-17','2025-11-17','PINGO DOCE ALFAMA',                      50.45,'debit', 8607.15,  2, 4,0,0),
( 93, 8,'2025-11-19','2025-11-19','GALP COMBUSTIVEIS SA',                   54.00,'debit', 8553.15,  3,12,0,0),
( 94, 8,'2025-11-22','2025-11-22','UBER* TRIP',                             11.90,'debit', 8541.25,  3,10,0,0),
( 95, 8,'2025-11-25','2025-11-25','BOLT* RIDE',                             17.60,'debit', 8523.65,  3,11,0,0),
( 96, 8,'2025-11-28','2025-11-28','TRANSFERENCIA PARA BPI 5678',           500.00,'debit', 8023.65,  9,32,0,1),
-- === December 2025 (Stmt 9) — Christmas subsidy ===
( 97, 9,'2025-12-01','2025-12-01','RENDA APARTAMENTO LISBOA',              800.00,'debit', 7223.65,  1, 1,0,0),
( 98, 9,'2025-12-03','2025-12-03','CONTINENTE MODELO COLOMBO',              85.60,'debit', 7138.05,  2, 3,0,0),
( 99, 9,'2025-12-05','2025-12-07','EDP COMERCIAL SA',                       45.30,'debit', 7092.75,  4,15,0,0),
(100, 9,'2025-12-07','2025-12-07','MEO COMUNICACOES SA',                    34.99,'debit', 7057.76,  4,16,0,0),
(101, 9,'2025-12-09','2025-12-09','AGUAS DE LISBOA EM',                     12.50,'debit', 7045.26,  4,17,0,0),
(102, 9,'2025-12-11','2025-12-11','METRO DE LISBOA EP',                     24.00,'debit', 7021.26,  3, 9,0,0),
(103, 9,'2025-12-15','2025-12-15','SALARIO EMPRESA ABC LDA DEZ/2025 INC. SUBSIDIO NATAL',3200.00,'credit',10221.26,8,30,0,0),
(104, 9,'2025-12-17','2025-12-17','PINGO DOCE ALFAMA',                      72.30,'debit',10148.96,  2, 4,0,0),
(105, 9,'2025-12-19','2025-12-19','GALP COMBUSTIVEIS SA',                   60.00,'debit',10088.96,  3,12,0,0),
(106, 9,'2025-12-22','2025-12-22','UBER* TRIP',                             15.20,'debit',10073.76,  3,10,0,0),
(107, 9,'2025-12-25','2025-12-25','FARMACIA WELLS COLOMBO',                 18.90,'debit',10054.86,  5,19,0,0),
(108, 9,'2025-12-28','2025-12-28','TRANSFERENCIA PARA BPI 5678',           500.00,'debit', 9554.86,  9,32,0,1),
-- === January 2026 (Stmt 10) ===
(109,10,'2026-01-01','2026-01-01','RENDA APARTAMENTO LISBOA',              800.00,'debit', 8754.86,  1, 1,0,0),
(110,10,'2026-01-03','2026-01-03','CONTINENTE MODELO COLOMBO',              67.80,'debit', 8687.06,  2, 3,0,0),
(111,10,'2026-01-05','2026-01-07','EDP COMERCIAL SA',                       46.50,'debit', 8640.56,  4,15,0,0),
(112,10,'2026-01-07','2026-01-07','MEO COMUNICACOES SA',                    34.99,'debit', 8605.57,  4,16,0,0),
(113,10,'2026-01-09','2026-01-09','AGUAS DE LISBOA EM',                     12.50,'debit', 8593.07,  4,17,0,0),
(114,10,'2026-01-11','2026-01-11','METRO DE LISBOA EP',                     24.00,'debit', 8569.07,  3, 9,0,0),
(115,10,'2026-01-15','2026-01-15','SALARIO EMPRESA ABC LDA JAN/2026',     1800.00,'credit',10369.07,  8,30,0,0),
(116,10,'2026-01-17','2026-01-17','PINGO DOCE ALFAMA',                      49.60,'debit',10319.47,  2, 4,0,0),
(117,10,'2026-01-19','2026-01-19','GALP COMBUSTIVEIS SA',                   56.00,'debit',10263.47,  3,12,0,0),
(118,10,'2026-01-22','2026-01-22','UBER* TRIP',                             10.30,'debit',10253.17,  3,10,0,0),
(119,10,'2026-01-25','2026-01-25','BOLT* RIDE',                             22.10,'debit',10231.07,  3,11,0,0),
(120,10,'2026-01-28','2026-01-28','TRANSFERENCIA PARA BPI 5678',           500.00,'debit', 9731.07,  9,32,0,1),
-- === February 2026 (Stmt 11) — Salary raise ===
(121,11,'2026-02-01','2026-02-01','RENDA APARTAMENTO LISBOA',              800.00,'debit', 8931.07,  1, 1,0,0),
(122,11,'2026-02-03','2026-02-03','CONTINENTE MODELO COLOMBO',              64.25,'debit', 8866.82,  2, 3,0,0),
(123,11,'2026-02-05','2026-02-07','EDP COMERCIAL SA',                       46.50,'debit', 8820.32,  4,15,0,0),
(124,11,'2026-02-07','2026-02-07','MEO COMUNICACOES SA',                    34.99,'debit', 8785.33,  4,16,0,0),
(125,11,'2026-02-09','2026-02-09','AGUAS DE LISBOA EM',                     12.50,'debit', 8772.83,  4,17,0,0),
(126,11,'2026-02-11','2026-02-11','METRO DE LISBOA EP',                     24.00,'debit', 8748.83,  3, 9,0,0),
(127,11,'2026-02-15','2026-02-15','SALARIO EMPRESA ABC LDA FEV/2026',     1850.00,'credit',10598.83,  8,30,0,0),
(128,11,'2026-02-17','2026-02-17','PINGO DOCE ALFAMA',                      46.80,'debit',10552.03,  2, 4,0,0),
(129,11,'2026-02-19','2026-02-19','GALP COMBUSTIVEIS SA',                   51.00,'debit',10501.03,  3,12,0,0),
(130,11,'2026-02-22','2026-02-22','UBER* TRIP',                              9.70,'debit',10491.33,  3,10,0,0),
(131,11,'2026-02-25','2026-02-25','FARMACIA WELLS COLOMBO',                 16.40,'debit',10474.93,  5,19,0,0),
(132,11,'2026-02-28','2026-02-28','TRANSFERENCIA PARA BPI 5678',           500.00,'debit', 9974.93,  9,32,0,1),
-- === March 2026 (Stmt 12) ===
(133,12,'2026-03-01','2026-03-01','RENDA APARTAMENTO LISBOA',              800.00,'debit', 9174.93,  1, 1,0,0),
(134,12,'2026-03-03','2026-03-03','CONTINENTE MODELO COLOMBO',              69.40,'debit', 9105.53,  2, 3,0,0),
(135,12,'2026-03-05','2026-03-07','EDP COMERCIAL SA',                       46.50,'debit', 9059.03,  4,15,0,0),
(136,12,'2026-03-07','2026-03-07','MEO COMUNICACOES SA',                    34.99,'debit', 9024.04,  4,16,0,0),
(137,12,'2026-03-09','2026-03-09','AGUAS DE LISBOA EM',                     12.50,'debit', 9011.54,  4,17,0,0),
(138,12,'2026-03-11','2026-03-11','METRO DE LISBOA EP',                     24.00,'debit', 8987.54,  3, 9,0,0),
(139,12,'2026-03-15','2026-03-15','SALARIO EMPRESA ABC LDA MAR/2026',     1850.00,'credit',10837.54,  8,30,0,0),
(140,12,'2026-03-17','2026-03-17','PINGO DOCE ALFAMA',                      55.20,'debit',10782.34,  2, 4,0,0),
(141,12,'2026-03-19','2026-03-19','GALP COMBUSTIVEIS SA',                   57.00,'debit',10725.34,  3,12,0,0),
(142,12,'2026-03-22','2026-03-22','UBER* TRIP',                             12.80,'debit',10712.54,  3,10,0,0),
(143,12,'2026-03-25','2026-03-25','BOLT* RIDE',                             14.30,'debit',10698.24,  3,11,0,0),
(144,12,'2026-03-28','2026-03-28','TRANSFERENCIA PARA BPI 5678',           500.00,'debit',10198.24,  9,32,0,1);

-- ============================================================
-- 5. TRANSACTIONS — BPI (8 per month, IDs 145–240)
--    1 Transfer in [internal] | 2 Ageas insurance [manual Health]
--    3 Clothing store | 4 Holmes gym [uncategorised]
--    5 Amazon | 6 Restaurant | 7 Lidl | 8 Misc
-- ============================================================
-- Seeding BPI transactions...

INSERT INTO [Transactions]
  ([Id],[StatementId],[DatePosting],[DateValue],[Description],[Amount],[Type],[Balance],[CategoryId],[CategoryRuleId],[CategorySetManually],[IsExcluded])
VALUES
-- === April 2025 (Stmt 13) ===
(145,13,'2025-04-02','2025-04-02','TRF ACTIVOBANK 5678',                   500.00,'credit',2600.00,  9,32,0,1),
(146,13,'2025-04-05','2025-04-05','AGEAS SEGUROS SAUDE',                    47.50,'debit', 2552.50,  5,NULL,1,0),
(147,13,'2025-04-08','2025-04-08','ZARA PORTUGAL COLOMBO',                  89.99,'debit', 2462.51,  7,27,0,0),
(148,13,'2025-04-12','2025-04-12','HOLMES PLACE GYM',                       39.99,'debit', 2422.52,NULL,NULL,0,0),
(149,13,'2025-04-15','2025-04-15','AMAZON PAYMENTS EU',                     34.50,'debit', 2388.02,  7,26,0,0),
(150,13,'2025-04-18','2025-04-18','RESTAURANTE O COMPADRE',                 28.30,'debit', 2359.72,  2, 8,0,0),
(151,13,'2025-04-22','2025-04-22','LIDL PORTUGAL SA',                       52.10,'debit', 2307.62,  2, 5,0,0),
(152,13,'2025-04-27','2025-04-27','DECATHLON LISBOA LOURES',                45.00,'debit', 2262.62,NULL,NULL,0,0),
-- === May 2025 (Stmt 14) ===
(153,14,'2025-05-02','2025-05-02','TRF ACTIVOBANK 5678',                   500.00,'credit',2762.62,  9,32,0,1),
(154,14,'2025-05-05','2025-05-05','AGEAS SEGUROS SAUDE',                    47.50,'debit', 2715.12,  5,NULL,1,0),
(155,14,'2025-05-08','2025-05-08','PRIMARK FORUM ALMADA',                   75.99,'debit', 2639.13,  7,28,0,0),
(156,14,'2025-05-12','2025-05-12','HOLMES PLACE GYM',                       39.99,'debit', 2599.14,NULL,NULL,0,0),
(157,14,'2025-05-15','2025-05-15','AMAZON PAYMENTS EU',                     28.70,'debit', 2570.44,  7,26,0,0),
(158,14,'2025-05-18','2025-05-18','RESTAURANTE CERVEJARIA',                 24.90,'debit', 2545.54,  2, 8,0,0),
(159,14,'2025-05-22','2025-05-22','LIDL PORTUGAL SA',                       49.85,'debit', 2495.69,  2, 5,0,0),
(160,14,'2025-05-27','2025-05-27','FNAC COLOMBO',                           35.00,'debit', 2460.69,  7,NULL,1,0),
-- === June 2025 (Stmt 15) ===
(161,15,'2025-06-02','2025-06-02','TRF ACTIVOBANK 5678',                   500.00,'credit',2960.69,  9,32,0,1),
(162,15,'2025-06-05','2025-06-05','AGEAS SEGUROS SAUDE',                    47.50,'debit', 2913.19,  5,NULL,1,0),
(163,15,'2025-06-08','2025-06-08','DECATHLON CASCAIS',                      65.40,'debit', 2847.79,NULL,NULL,0,0),
(164,15,'2025-06-12','2025-06-12','HOLMES PLACE GYM',                       39.99,'debit', 2807.80,NULL,NULL,0,0),
(165,15,'2025-06-15','2025-06-15','AMAZON PAYMENTS EU',                     45.20,'debit', 2762.60,  7,26,0,0),
(166,15,'2025-06-18','2025-06-18','RESTAURANTE SOLAR DOS PRESUNTOS',        32.80,'debit', 2729.80,  2, 8,0,0),
(167,15,'2025-06-22','2025-06-22','LIDL PORTUGAL SA',                       55.40,'debit', 2674.40,  2, 5,0,0),
(168,15,'2025-06-27','2025-06-27','WORTEN ELECTRONICA',                     60.00,'debit', 2614.40,  7,29,0,0),
-- === July 2025 (Stmt 16) ===
(169,16,'2025-07-02','2025-07-02','TRF ACTIVOBANK 5678',                   500.00,'credit',3114.40,  9,32,0,1),
(170,16,'2025-07-05','2025-07-05','AGEAS SEGUROS SAUDE',                    47.50,'debit', 3066.90,  5,NULL,1,0),
(171,16,'2025-07-08','2025-07-08','PRIMARK FORUM ALMADA',                  120.00,'debit', 2946.90,  7,28,0,0),
(172,16,'2025-07-12','2025-07-12','HOLMES PLACE GYM',                       39.99,'debit', 2906.91,NULL,NULL,0,0),
(173,16,'2025-07-15','2025-07-15','AMAZON PAYMENTS EU',                     22.30,'debit', 2884.61,  7,26,0,0),
(174,16,'2025-07-18','2025-07-18','RESTAURANTE DA PRAIA',                   35.60,'debit', 2849.01,  2, 8,0,0),
(175,16,'2025-07-22','2025-07-22','LIDL PORTUGAL SA',                       48.75,'debit', 2800.26,  2, 5,0,0),
(176,16,'2025-07-27','2025-07-27','DECATHLON LISBOA LOURES',                65.00,'debit', 2735.26,NULL,NULL,0,0),
-- === August 2025 (Stmt 17) ===
(177,17,'2025-08-02','2025-08-02','TRF ACTIVOBANK 5678',                   500.00,'credit',3235.26,  9,32,0,1),
(178,17,'2025-08-05','2025-08-05','AGEAS SEGUROS SAUDE',                    47.50,'debit', 3187.76,  5,NULL,1,0),
(179,17,'2025-08-08','2025-08-08','PRIMARK FORUM ALMADA',                   55.80,'debit', 3131.96,  7,28,0,0),
(180,17,'2025-08-12','2025-08-12','HOLMES PLACE GYM',                       39.99,'debit', 3091.97,NULL,NULL,0,0),
(181,17,'2025-08-15','2025-08-15','AMAZON PAYMENTS EU',                     55.80,'debit', 3036.17,  7,26,0,0),
(182,17,'2025-08-18','2025-08-18','RESTAURANTE MARISQUEIRA',                18.90,'debit', 3017.27,  2, 8,0,0),
(183,17,'2025-08-22','2025-08-22','LIDL PORTUGAL SA',                       52.30,'debit', 2964.97,  2, 5,0,0),
(184,17,'2025-08-27','2025-08-27','CINEMA NOS COLOMBO',                     22.00,'debit', 2942.97,  6,24,0,0),
-- === September 2025 (Stmt 18) ===
(185,18,'2025-09-02','2025-09-02','TRF ACTIVOBANK 5678',                   500.00,'credit',3442.97,  9,32,0,1),
(186,18,'2025-09-05','2025-09-05','AGEAS SEGUROS SAUDE',                    47.50,'debit', 3395.47,  5,NULL,1,0),
(187,18,'2025-09-08','2025-09-08','ZARA PORTUGAL COLOMBO',                  95.00,'debit', 3300.47,  7,27,0,0),
(188,18,'2025-09-12','2025-09-12','HOLMES PLACE GYM',                       39.99,'debit', 3260.48,NULL,NULL,0,0),
(189,18,'2025-09-15','2025-09-15','AMAZON PAYMENTS EU',                     38.40,'debit', 3222.08,  7,26,0,0),
(190,18,'2025-09-18','2025-09-18','RESTAURANTE O COMPADRE',                 29.70,'debit', 3192.38,  2, 8,0,0),
(191,18,'2025-09-22','2025-09-22','LIDL PORTUGAL SA',                       47.55,'debit', 3144.83,  2, 5,0,0),
(192,18,'2025-09-27','2025-09-27','DECATHLON CASCAIS',                      45.00,'debit', 3099.83,NULL,NULL,0,0),
-- === October 2025 (Stmt 19) ===
(193,19,'2025-10-02','2025-10-02','TRF ACTIVOBANK 5678',                   500.00,'credit',3599.83,  9,32,0,1),
(194,19,'2025-10-05','2025-10-05','AGEAS SEGUROS SAUDE',                    47.50,'debit', 3552.33,  5,NULL,1,0),
(195,19,'2025-10-08','2025-10-08','WORTEN ELECTRONICA',                    139.00,'debit', 3413.33,  7,29,0,0),
(196,19,'2025-10-12','2025-10-12','HOLMES PLACE GYM',                       39.99,'debit', 3373.34,NULL,NULL,0,0),
(197,19,'2025-10-15','2025-10-15','AMAZON PAYMENTS EU',                     29.90,'debit', 3343.44,  7,26,0,0),
(198,19,'2025-10-18','2025-10-18','RESTAURANTE CERVEJARIA',                 33.80,'debit', 3309.64,  2, 8,0,0),
(199,19,'2025-10-22','2025-10-22','LIDL PORTUGAL SA',                       51.25,'debit', 3258.39,  2, 5,0,0),
(200,19,'2025-10-27','2025-10-27','FNAC COLOMBO',                           45.00,'debit', 3213.39,  7,NULL,1,0),
-- === November 2025 (Stmt 20) ===
(201,20,'2025-11-02','2025-11-02','TRF ACTIVOBANK 5678',                   500.00,'credit',3713.39,  9,32,0,1),
(202,20,'2025-11-05','2025-11-05','AGEAS SEGUROS SAUDE',                    47.50,'debit', 3665.89,  5,NULL,1,0),
(203,20,'2025-11-08','2025-11-08','ZARA PORTUGAL COLOMBO',                  68.00,'debit', 3597.89,  7,27,0,0),
(204,20,'2025-11-12','2025-11-12','HOLMES PLACE GYM',                       39.99,'debit', 3557.90,NULL,NULL,0,0),
(205,20,'2025-11-15','2025-11-15','AMAZON PAYMENTS EU',                     18.90,'debit', 3539.00,  7,26,0,0),
(206,20,'2025-11-18','2025-11-18','RESTAURANTE O COMPADRE',                 27.40,'debit', 3511.60,  2, 8,0,0),
(207,20,'2025-11-22','2025-11-22','LIDL PORTUGAL SA',                       53.60,'debit', 3458.00,  2, 5,0,0),
(208,20,'2025-11-27','2025-11-27','PRIMARK FORUM ALMADA',                   45.00,'debit', 3413.00,  7,28,0,0),
-- === December 2025 (Stmt 21) — Christmas spending ===
(209,21,'2025-12-02','2025-12-02','TRF ACTIVOBANK 5678',                   500.00,'credit',3913.00,  9,32,0,1),
(210,21,'2025-12-05','2025-12-05','AGEAS SEGUROS SAUDE',                    47.50,'debit', 3865.50,  5,NULL,1,0),
(211,21,'2025-12-08','2025-12-08','FNAC COLOMBO',                          150.00,'debit', 3715.50,  7,NULL,1,0),
(212,21,'2025-12-12','2025-12-12','HOLMES PLACE GYM',                       39.99,'debit', 3675.51,NULL,NULL,0,0),
(213,21,'2025-12-15','2025-12-15','AMAZON PAYMENTS EU',                     89.90,'debit', 3585.61,  7,26,0,0),
(214,21,'2025-12-18','2025-12-18','RESTAURANTE SOLAR DOS PRESUNTOS',        55.80,'debit', 3529.81,  2, 8,0,0),
(215,21,'2025-12-22','2025-12-22','LIDL PORTUGAL SA',                       65.40,'debit', 3464.41,  2, 5,0,0),
(216,21,'2025-12-27','2025-12-27','WORTEN ELECTRONICA',                     55.00,'debit', 3409.41,  7,29,0,0),
-- === January 2026 (Stmt 22) ===
(217,22,'2026-01-02','2026-01-02','TRF ACTIVOBANK 5678',                   500.00,'credit',3909.41,  9,32,0,1),
(218,22,'2026-01-05','2026-01-05','AGEAS SEGUROS SAUDE',                    47.50,'debit', 3861.91,  5,NULL,1,0),
(219,22,'2026-01-08','2026-01-08','PRIMARK FORUM ALMADA',                   35.00,'debit', 3826.91,  7,28,0,0),
(220,22,'2026-01-12','2026-01-12','HOLMES PLACE GYM',                       39.99,'debit', 3786.92,NULL,NULL,0,0),
(221,22,'2026-01-15','2026-01-15','AMAZON PAYMENTS EU',                     25.60,'debit', 3761.32,  7,26,0,0),
(222,22,'2026-01-18','2026-01-18','RESTAURANTE CERVEJARIA',                 22.40,'debit', 3738.92,  2, 8,0,0),
(223,22,'2026-01-22','2026-01-22','LIDL PORTUGAL SA',                       48.90,'debit', 3690.02,  2, 5,0,0),
(224,22,'2026-01-27','2026-01-27','CINEMA NOS COLOMBO',                     18.00,'debit', 3672.02,  6,24,0,0),
-- === February 2026 (Stmt 23) ===
(225,23,'2026-02-02','2026-02-02','TRF ACTIVOBANK 5678',                   500.00,'credit',4172.02,  9,32,0,1),
(226,23,'2026-02-05','2026-02-05','AGEAS SEGUROS SAUDE',                    47.50,'debit', 4124.52,  5,NULL,1,0),
(227,23,'2026-02-08','2026-02-08','ZARA PORTUGAL COLOMBO',                  55.00,'debit', 4069.52,  7,27,0,0),
(228,23,'2026-02-12','2026-02-12','HOLMES PLACE GYM',                       39.99,'debit', 4029.53,NULL,NULL,0,0),
(229,23,'2026-02-15','2026-02-15','AMAZON PAYMENTS EU',                     19.80,'debit', 4009.73,  7,26,0,0),
(230,23,'2026-02-18','2026-02-18','RESTAURANTE O COMPADRE',                 19.80,'debit', 3989.93,  2, 8,0,0),
(231,23,'2026-02-22','2026-02-22','LIDL PORTUGAL SA',                       45.30,'debit', 3944.63,  2, 5,0,0),
(232,23,'2026-02-27','2026-02-27','DECATHLON CASCAIS',                      35.00,'debit', 3909.63,NULL,NULL,0,0),
-- === March 2026 (Stmt 24) ===
(233,24,'2026-03-02','2026-03-02','TRF ACTIVOBANK 5678',                   500.00,'credit',4409.63,  9,32,0,1),
(234,24,'2026-03-05','2026-03-05','AGEAS SEGUROS SAUDE',                    47.50,'debit', 4362.13,  5,NULL,1,0),
(235,24,'2026-03-08','2026-03-08','PRIMARK FORUM ALMADA',                   49.99,'debit', 4312.14,  7,28,0,0),
(236,24,'2026-03-12','2026-03-12','HOLMES PLACE GYM',                       39.99,'debit', 4272.15,NULL,NULL,0,0),
(237,24,'2026-03-15','2026-03-15','AMAZON PAYMENTS EU',                     44.20,'debit', 4227.95,  7,26,0,0),
(238,24,'2026-03-18','2026-03-18','RESTAURANTE DA COSTA',                   31.70,'debit', 4196.25,  2, 8,0,0),
(239,24,'2026-03-22','2026-03-22','LIDL PORTUGAL SA',                       50.80,'debit', 4145.45,  2, 5,0,0),
(240,24,'2026-03-27','2026-03-27','FNAC COLOMBO',                           30.00,'debit', 4115.45,  7,NULL,1,0);

-- ============================================================
-- 6. TRANSACTIONS — Revolut (6 per month, IDs 241–312)
--    1 Top-up [internal] | 2 Netflix | 3 Spotify
--    4 Amazon | 5 Steam/Subscription (varies) | 6 Misc online
-- ============================================================
-- Seeding Revolut transactions...

INSERT INTO [Transactions]
  ([Id],[StatementId],[DatePosting],[DateValue],[Description],[Amount],[Type],[Balance],[CategoryId],[CategoryRuleId],[CategorySetManually],[IsExcluded])
VALUES
-- === April 2025 (Stmt 25) ===
(241,25,'2025-04-03','2025-04-03','TOP-UP REVOLUT',                        200.00,'credit',1050.00,  9,32,0,1),
(242,25,'2025-04-05','2025-04-05','NETFLIX INTL BV',                        13.99,'debit', 1036.01,  6,22,0,0),
(243,25,'2025-04-07','2025-04-07','SPOTIFY AB',                              5.99,'debit', 1030.02,  6,23,0,0),
(244,25,'2025-04-10','2025-04-10','AMAZON MARKETPLACE EU',                  38.50,'debit',  991.52,  7,26,0,0),
(245,25,'2025-04-15','2025-04-15','STEAM GAMES',                            24.99,'debit',  966.53,  6,25,0,0),
(246,25,'2025-04-20','2025-04-20','PAYPAL*EBAY MARKETPLACE',                45.00,'debit',  921.53,NULL,NULL,0,0),
-- === May 2025 (Stmt 26) ===
(247,26,'2025-05-03','2025-05-03','TOP-UP REVOLUT',                        200.00,'credit',1121.53,  9,32,0,1),
(248,26,'2025-05-05','2025-05-05','NETFLIX INTL BV',                        13.99,'debit', 1107.54,  6,22,0,0),
(249,26,'2025-05-07','2025-05-07','SPOTIFY AB',                              5.99,'debit', 1101.55,  6,23,0,0),
(250,26,'2025-05-10','2025-05-10','AMAZON MARKETPLACE EU',                  52.30,'debit', 1049.25,  7,26,0,0),
(251,26,'2025-05-15','2025-05-15','HUMBLE BUNDLE',                          14.99,'debit', 1034.26,  6,NULL,1,0),
(252,26,'2025-05-20','2025-05-20','ALIEXPRESS',                             55.00,'debit',  979.26,  7,NULL,1,0),
-- === June 2025 (Stmt 27) ===
(253,27,'2025-06-03','2025-06-03','TOP-UP REVOLUT',                        200.00,'credit',1179.26,  9,32,0,1),
(254,27,'2025-06-05','2025-06-05','NETFLIX INTL BV',                        13.99,'debit', 1165.27,  6,22,0,0),
(255,27,'2025-06-07','2025-06-07','SPOTIFY AB',                              5.99,'debit', 1159.28,  6,23,0,0),
(256,27,'2025-06-10','2025-06-10','AMAZON MARKETPLACE EU',                  28.40,'debit', 1130.88,  7,26,0,0),
(257,27,'2025-06-15','2025-06-15','STEAM GAMES',                            19.99,'debit', 1110.89,  6,25,0,0),
(258,27,'2025-06-20','2025-06-20','BOOKING.COM',                           180.00,'debit',  930.89,NULL,NULL,0,0),
-- === July 2025 (Stmt 28) ===
(259,28,'2025-07-03','2025-07-03','TOP-UP REVOLUT',                        200.00,'credit',1130.89,  9,32,0,1),
(260,28,'2025-07-05','2025-07-05','NETFLIX INTL BV',                        13.99,'debit', 1116.90,  6,22,0,0),
(261,28,'2025-07-07','2025-07-07','SPOTIFY AB',                              5.99,'debit', 1110.91,  6,23,0,0),
(262,28,'2025-07-10','2025-07-10','AMAZON MARKETPLACE EU',                  45.00,'debit', 1065.91,  7,26,0,0),
(263,28,'2025-07-15','2025-07-15','HUMBLE BUNDLE',                          12.99,'debit', 1052.92,  6,NULL,1,0),
(264,28,'2025-07-20','2025-07-20','AIRBNB',                                120.00,'debit',  932.92,NULL,NULL,0,0),
-- === August 2025 (Stmt 29) ===
(265,29,'2025-08-03','2025-08-03','TOP-UP REVOLUT',                        300.00,'credit',1232.92,  9,32,0,1),
(266,29,'2025-08-05','2025-08-05','NETFLIX INTL BV',                        13.99,'debit', 1218.93,  6,22,0,0),
(267,29,'2025-08-07','2025-08-07','SPOTIFY AB',                              5.99,'debit', 1212.94,  6,23,0,0),
(268,29,'2025-08-10','2025-08-10','AMAZON MARKETPLACE EU',                  38.50,'debit', 1174.44,  7,26,0,0),
(269,29,'2025-08-15','2025-08-15','APPLE.COM/BILL',                          5.99,'debit', 1168.45,  6,NULL,1,0),
(270,29,'2025-08-20','2025-08-20','PAYPAL TRANSFER',                        95.00,'debit', 1073.45, 10,NULL,1,0),
-- === September 2025 (Stmt 30) ===
(271,30,'2025-09-03','2025-09-03','TOP-UP REVOLUT',                        200.00,'credit',1273.45,  9,32,0,1),
(272,30,'2025-09-05','2025-09-05','NETFLIX INTL BV',                        13.99,'debit', 1259.46,  6,22,0,0),
(273,30,'2025-09-07','2025-09-07','SPOTIFY AB',                              5.99,'debit', 1253.47,  6,23,0,0),
(274,30,'2025-09-10','2025-09-10','AMAZON MARKETPLACE EU',                  22.40,'debit', 1231.07,  7,26,0,0),
(275,30,'2025-09-15','2025-09-15','STEAM GAMES',                            14.99,'debit', 1216.08,  6,25,0,0),
(276,30,'2025-09-20','2025-09-20','UDEMY COURSE',                           55.00,'debit', 1161.08,  6,NULL,1,0),
-- === October 2025 (Stmt 31) ===
(277,31,'2025-10-03','2025-10-03','TOP-UP REVOLUT',                        200.00,'credit',1361.08,  9,32,0,1),
(278,31,'2025-10-05','2025-10-05','NETFLIX INTL BV',                        13.99,'debit', 1347.09,  6,22,0,0),
(279,31,'2025-10-07','2025-10-07','SPOTIFY AB',                              5.99,'debit', 1341.10,  6,23,0,0),
(280,31,'2025-10-10','2025-10-10','AMAZON MARKETPLACE EU',                  48.00,'debit', 1293.10,  7,26,0,0),
(281,31,'2025-10-15','2025-10-15','STEAM AUTUMN SALE',                      24.99,'debit', 1268.11,  6,25,0,0),
(282,31,'2025-10-20','2025-10-20','DIGITALOCEAN INC',                       12.00,'debit', 1256.11,NULL,NULL,0,0),
-- === November 2025 (Stmt 32) — Black Friday ===
(283,32,'2025-11-03','2025-11-03','TOP-UP REVOLUT',                        200.00,'credit',1456.11,  9,32,0,1),
(284,32,'2025-11-05','2025-11-05','NETFLIX INTL BV',                        13.99,'debit', 1442.12,  6,22,0,0),
(285,32,'2025-11-07','2025-11-07','SPOTIFY AB',                              5.99,'debit', 1436.13,  6,23,0,0),
(286,32,'2025-11-10','2025-11-10','AMAZON BLACK FRIDAY',                    85.00,'debit', 1351.13,  7,26,0,0),
(287,32,'2025-11-15','2025-11-15','MICROSOFT XBOX',                          9.99,'debit', 1341.14,  6,NULL,1,0),
(288,32,'2025-11-20','2025-11-20','ALIEXPRESS',                             15.00,'debit', 1326.14,  7,NULL,1,0),
-- === December 2025 (Stmt 33) ===
(289,33,'2025-12-03','2025-12-03','TOP-UP REVOLUT',                        200.00,'credit',1526.14,  9,32,0,1),
(290,33,'2025-12-05','2025-12-05','NETFLIX INTL BV',                        13.99,'debit', 1512.15,  6,22,0,0),
(291,33,'2025-12-07','2025-12-07','SPOTIFY AB',                              5.99,'debit', 1506.16,  6,23,0,0),
(292,33,'2025-12-10','2025-12-10','AMAZON MARKETPLACE EU',                  85.00,'debit', 1421.16,  7,26,0,0),
(293,33,'2025-12-15','2025-12-15','STEAM WINTER SALE',                      29.99,'debit', 1391.17,  6,25,0,0),
(294,33,'2025-12-20','2025-12-20','ETSY MARKETPLACE',                       25.00,'debit', 1366.17,  7,NULL,1,0),
-- === January 2026 (Stmt 34) ===
(295,34,'2026-01-03','2026-01-03','TOP-UP REVOLUT',                        200.00,'credit',1566.17,  9,32,0,1),
(296,34,'2026-01-05','2026-01-05','NETFLIX INTL BV',                        13.99,'debit', 1552.18,  6,22,0,0),
(297,34,'2026-01-07','2026-01-07','SPOTIFY AB',                              5.99,'debit', 1546.19,  6,23,0,0),
(298,34,'2026-01-10','2026-01-10','AMAZON MARKETPLACE EU',                  32.40,'debit', 1513.79,  7,26,0,0),
(299,34,'2026-01-15','2026-01-15','STEAM GAMES',                             9.99,'debit', 1503.80,  6,25,0,0),
(300,34,'2026-01-20','2026-01-20','PAYPAL TRANSFER',                        55.00,'debit', 1448.80,NULL,NULL,0,0),
-- === February 2026 (Stmt 35) ===
(301,35,'2026-02-03','2026-02-03','TOP-UP REVOLUT',                        200.00,'credit',1648.80,  9,32,0,1),
(302,35,'2026-02-05','2026-02-05','NETFLIX INTL BV',                        13.99,'debit', 1634.81,  6,22,0,0),
(303,35,'2026-02-07','2026-02-07','SPOTIFY AB',                              5.99,'debit', 1628.82,  6,23,0,0),
(304,35,'2026-02-10','2026-02-10','APPLE.COM/BILL',                          5.99,'debit', 1622.83,  6,NULL,1,0),
(305,35,'2026-02-15','2026-02-15','YOUTUBE PREMIUM',                         5.99,'debit', 1616.84,  6,NULL,1,0),
(306,35,'2026-02-20','2026-02-20','PAYPAL TRANSFER',                        45.00,'debit', 1571.84,NULL,NULL,0,0),
-- === March 2026 (Stmt 36) ===
(307,36,'2026-03-03','2026-03-03','TOP-UP REVOLUT',                        200.00,'credit',1771.84,  9,32,0,1),
(308,36,'2026-03-05','2026-03-05','NETFLIX INTL BV',                        13.99,'debit', 1757.85,  6,22,0,0),
(309,36,'2026-03-07','2026-03-07','SPOTIFY AB',                              5.99,'debit', 1751.86,  6,23,0,0),
(310,36,'2026-03-10','2026-03-10','AMAZON MARKETPLACE EU',                  38.50,'debit', 1713.36,  7,26,0,0),
(311,36,'2026-03-15','2026-03-15','STEAM GAMES',                            14.99,'debit', 1698.37,  6,25,0,0),
(312,36,'2026-03-20','2026-03-20','ALIEXPRESS',                             20.00,'debit', 1678.37,  7,NULL,1,0);


-- ============================================================
-- 7. SALARY PROFILES
-- ============================================================
-- Seeding salary profiles...

INSERT INTO [SalaryProfiles] ([Id],[Name],[Description],[HourlyRateFormula]) VALUES
(1,'Main Job',       'Primary employment','days'),
(2,'Side Consulting','Freelance consulting and project work','days');


-- ============================================================
-- 8. SALARY ITEM CATEGORIES
-- ============================================================
-- Seeding salary item categories...

INSERT INTO [SalaryItemCategories] ([Id],[SalaryProfileId],[Name],[Color],[ItemType],[IsProtected]) VALUES
-- Profile 1
(1,1,'Vencimento Base',    '#10b981','income',   0),
(2,1,'Subsidio Refeicao',  '#34d399','income',   0),
(3,1,'Subsidio Ferias/Natal','#6ee7b7','income', 0),
(4,1,'Seguranca Social',   '#f87171','deduction',0),
(5,1,'IRS Retencao',       '#fca5a5','tax',      0),
(6,1,'Sindicato',          '#fb923c','deduction',0),
-- Profile 2
(7,2,'Honorarios',         '#60a5fa','income',   0),
(8,2,'IRS Retencao Fonte', '#93c5fd','tax',      0);


-- ============================================================
-- 9. SALARY SLIPS  (12 per profile = 24 total)
--    Profile 1: regular months gross=1500.00 net=1100.00
--               July/Dec gross=2800.00 net=2000.00 (subsidy)
--    Profile 2: freelance, variable gross, 25% IRS
-- ============================================================
-- Seeding salary slips...

INSERT INTO [SalarySlips]
  ([Id],[SalaryProfileId],[Period],[GrossAmount],[NetAmount],[Notes],[SourceFile],[PdfPath],[FileHash],[ImportedAt],[BaseAmount],[HoursWorked],[HourlyRate],[TotalEspecie])
VALUES
-- Profile 1 — regular months
( 1,1,'2025-04-01',1500.00,1100.00,NULL,'sample_slip_2025_04.pdf',NULL,NULL,'2025-05-03 10:00:00',1400.00,176.00,8.00,100.00),
( 2,1,'2025-05-01',1500.00,1100.00,NULL,'sample_slip_2025_05.pdf',NULL,NULL,'2025-06-02 10:00:00',1400.00,176.00,8.00,100.00),
( 3,1,'2025-06-01',1500.00,1100.00,NULL,'sample_slip_2025_06.pdf',NULL,NULL,'2025-07-02 10:00:00',1400.00,176.00,8.00,100.00),
-- Profile 1 — July: vacation subsidy
( 4,1,'2025-07-01',2800.00,2000.00,'Demo note: includes vacation subsidy','sample_slip_2025_07.pdf',NULL,NULL,'2025-08-02 10:00:00',1400.00,176.00,8.00,100.00),
-- Profile 1 — regular months
( 5,1,'2025-08-01',1500.00,1100.00,NULL,'sample_slip_2025_08.pdf',NULL,NULL,'2025-09-03 10:00:00',1400.00,176.00,8.00,100.00),
( 6,1,'2025-09-01',1500.00,1100.00,NULL,'sample_slip_2025_09.pdf',NULL,NULL,'2025-10-02 10:00:00',1400.00,176.00,8.00,100.00),
( 7,1,'2025-10-01',1500.00,1100.00,NULL,'sample_slip_2025_10.pdf',NULL,NULL,'2025-11-03 10:00:00',1400.00,176.00,8.00,100.00),
( 8,1,'2025-11-01',1500.00,1100.00,NULL,'sample_slip_2025_11.pdf',NULL,NULL,'2025-12-02 10:00:00',1400.00,176.00,8.00,100.00),
-- Profile 1 — December: Christmas subsidy
( 9,1,'2025-12-01',2800.00,2000.00,'Demo note: includes Christmas subsidy','sample_slip_2025_12.pdf',NULL,NULL,'2026-01-03 10:00:00',1400.00,176.00,8.00,100.00),
-- Profile 1 — regular months (salary adjustment Feb 2026)
(10,1,'2026-01-01',1500.00,1100.00,NULL,'sample_slip_2026_01.pdf',NULL,NULL,'2026-02-02 10:00:00',1400.00,176.00,8.00,100.00),
(11,1,'2026-02-01',1600.00,1150.00,'Demo note: salary adjustment','sample_slip_2026_02.pdf',NULL,NULL,'2026-03-03 10:00:00',1500.00,176.00,8.25,100.00),
(12,1,'2026-03-01',1600.00,1150.00,NULL,'sample_slip_2026_03.pdf',NULL,NULL,'2026-04-02 10:00:00',1500.00,176.00,8.25,100.00),
-- Profile 2 — freelance (variable)
(13,2,'2025-04-01', 500.00, 375.00,NULL,'sample_consulting_2025_04.pdf',NULL,NULL,'2025-05-05 10:00:00',NULL,NULL,NULL,NULL),
(14,2,'2025-05-01', 350.00, 262.50,NULL,'sample_consulting_2025_05.pdf',NULL,NULL,'2025-06-04 10:00:00',NULL,NULL,NULL,NULL),
(15,2,'2025-06-01', 600.00, 450.00,NULL,'sample_consulting_2025_06.pdf',NULL,NULL,'2025-07-04 10:00:00',NULL,NULL,NULL,NULL),
(16,2,'2025-07-01', 250.00, 187.50,NULL,'sample_consulting_2025_07.pdf',NULL,NULL,'2025-08-04 10:00:00',NULL,NULL,NULL,NULL),
(17,2,'2025-08-01', 150.00, 112.50,NULL,'sample_consulting_2025_08.pdf',NULL,NULL,'2025-09-04 10:00:00',NULL,NULL,NULL,NULL),
(18,2,'2025-09-01', 450.00, 337.50,NULL,'sample_consulting_2025_09.pdf',NULL,NULL,'2025-10-04 10:00:00',NULL,NULL,NULL,NULL),
(19,2,'2025-10-01', 500.00, 375.00,NULL,'sample_consulting_2025_10.pdf',NULL,NULL,'2025-11-05 10:00:00',NULL,NULL,NULL,NULL),
(20,2,'2025-11-01', 350.00, 262.50,NULL,'sample_consulting_2025_11.pdf',NULL,NULL,'2025-12-04 10:00:00',NULL,NULL,NULL,NULL),
(21,2,'2025-12-01', 200.00, 150.00,NULL,'sample_consulting_2025_12.pdf',NULL,NULL,'2026-01-05 10:00:00',NULL,NULL,NULL,NULL),
(22,2,'2026-01-01', 400.00, 300.00,NULL,'sample_consulting_2026_01.pdf',NULL,NULL,'2026-02-04 10:00:00',NULL,NULL,NULL,NULL),
(23,2,'2026-02-01', 550.00, 412.50,NULL,'sample_consulting_2026_02.pdf',NULL,NULL,'2026-03-05 10:00:00',NULL,NULL,NULL,NULL),
(24,2,'2026-03-01', 480.00, 360.00,NULL,'sample_consulting_2026_03.pdf',NULL,NULL,'2026-04-04 10:00:00',NULL,NULL,NULL,NULL);


-- ============================================================
-- 10. SALARY LINE ITEMS
--     Profile 1 regular (5 items × 10 months) + special (6 items × 2)
--     Profile 2 (2 items × 12 months)
-- ============================================================
-- Seeding salary line items...

INSERT INTO [SalaryLineItems]
  ([Id],[SalarySlipId],[SalaryItemCategoryId],[Amount],[SortOrder],[Quantity],[UnitValue],[Percentage],[IncidenciaBase])
VALUES
-- ---- Slip 1 (Apr 2025, regular) ----
( 1, 1,1,1400.00,1,NULL,  NULL,  NULL,    NULL),
( 2, 1,2, 167.86,2,22.00, 7.63,  NULL,    NULL),
( 3, 1,4, 172.46,3,NULL,  NULL,  11.00,1567.86),
( 4, 1,5, 252.00,4,NULL,  NULL,  16.08,1567.86),
( 5, 1,6,  14.00,5,NULL,  NULL,   1.00,1400.00),
-- ---- Slip 2 (May 2025, regular) ----
( 6, 2,1,1400.00,1,NULL,  NULL,  NULL,    NULL),
( 7, 2,2, 167.86,2,22.00, 7.63,  NULL,    NULL),
( 8, 2,4, 172.46,3,NULL,  NULL,  11.00,1567.86),
( 9, 2,5, 252.00,4,NULL,  NULL,  16.08,1567.86),
(10, 2,6,  14.00,5,NULL,  NULL,   1.00,1400.00),
-- ---- Slip 3 (Jun 2025, regular) ----
(11, 3,1,1400.00,1,NULL,  NULL,  NULL,    NULL),
(12, 3,2, 167.86,2,22.00, 7.63,  NULL,    NULL),
(13, 3,4, 172.46,3,NULL,  NULL,  11.00,1567.86),
(14, 3,5, 252.00,4,NULL,  NULL,  16.08,1567.86),
(15, 3,6,  14.00,5,NULL,  NULL,   1.00,1400.00),
-- ---- Slip 4 (Jul 2025, VACATION SUBSIDY — 6 items) ----
(16, 4,1,1400.00,1,NULL,  NULL,  NULL,    NULL),
(17, 4,2, 167.86,2,22.00, 7.63,  NULL,    NULL),
(18, 4,3,1400.00,3,NULL,  NULL,  NULL,    NULL),
(19, 4,4, 326.46,4,NULL,  NULL,  11.00,2967.86),
(20, 4,5, 504.54,5,NULL,  NULL,  17.00,2967.86),
(21, 4,6,  14.00,6,NULL,  NULL,   1.00,1400.00),
-- ---- Slip 5 (Aug 2025, regular) ----
(22, 5,1,1400.00,1,NULL,  NULL,  NULL,    NULL),
(23, 5,2, 167.86,2,22.00, 7.63,  NULL,    NULL),
(24, 5,4, 172.46,3,NULL,  NULL,  11.00,1567.86),
(25, 5,5, 252.00,4,NULL,  NULL,  16.08,1567.86),
(26, 5,6,  14.00,5,NULL,  NULL,   1.00,1400.00),
-- ---- Slip 6 (Sep 2025, regular) ----
(27, 6,1,1400.00,1,NULL,  NULL,  NULL,    NULL),
(28, 6,2, 167.86,2,22.00, 7.63,  NULL,    NULL),
(29, 6,4, 172.46,3,NULL,  NULL,  11.00,1567.86),
(30, 6,5, 252.00,4,NULL,  NULL,  16.08,1567.86),
(31, 6,6,  14.00,5,NULL,  NULL,   1.00,1400.00),
-- ---- Slip 7 (Oct 2025, regular) ----
(32, 7,1,1400.00,1,NULL,  NULL,  NULL,    NULL),
(33, 7,2, 167.86,2,22.00, 7.63,  NULL,    NULL),
(34, 7,4, 172.46,3,NULL,  NULL,  11.00,1567.86),
(35, 7,5, 252.00,4,NULL,  NULL,  16.08,1567.86),
(36, 7,6,  14.00,5,NULL,  NULL,   1.00,1400.00),
-- ---- Slip 8 (Nov 2025, regular) ----
(37, 8,1,1400.00,1,NULL,  NULL,  NULL,    NULL),
(38, 8,2, 167.86,2,22.00, 7.63,  NULL,    NULL),
(39, 8,4, 172.46,3,NULL,  NULL,  11.00,1567.86),
(40, 8,5, 252.00,4,NULL,  NULL,  16.08,1567.86),
(41, 8,6,  14.00,5,NULL,  NULL,   1.00,1400.00),
-- ---- Slip 9 (Dec 2025, CHRISTMAS SUBSIDY — 6 items) ----
(42, 9,1,1400.00,1,NULL,  NULL,  NULL,    NULL),
(43, 9,2, 167.86,2,22.00, 7.63,  NULL,    NULL),
(44, 9,3,1400.00,3,NULL,  NULL,  NULL,    NULL),
(45, 9,4, 326.46,4,NULL,  NULL,  11.00,2967.86),
(46, 9,5, 504.54,5,NULL,  NULL,  17.00,2967.86),
(47, 9,6,  14.00,6,NULL,  NULL,   1.00,1400.00),
-- ---- Slip 10 (Jan 2026, regular) ----
(48,10,1,1400.00,1,NULL,  NULL,  NULL,    NULL),
(49,10,2, 167.86,2,22.00, 7.63,  NULL,    NULL),
(50,10,4, 172.46,3,NULL,  NULL,  11.00,1567.86),
(51,10,5, 252.00,4,NULL,  NULL,  16.08,1567.86),
(52,10,6,  14.00,5,NULL,  NULL,   1.00,1400.00),
-- ---- Slip 11 (Feb 2026, after raise) ----
(53,11,1,1450.00,1,NULL,  NULL,  NULL,    NULL),
(54,11,2, 167.86,2,22.00, 7.63,  NULL,    NULL),
(55,11,4, 178.46,3,NULL,  NULL,  11.00,1617.86),
(56,11,5, 259.00,4,NULL,  NULL,  16.01,1617.86),
(57,11,6,  14.00,5,NULL,  NULL,   1.00,1450.00),
-- ---- Slip 12 (Mar 2026, after raise) ----
(58,12,1,1450.00,1,NULL,  NULL,  NULL,    NULL),
(59,12,2, 167.86,2,22.00, 7.63,  NULL,    NULL),
(60,12,4, 178.46,3,NULL,  NULL,  11.00,1617.86),
(61,12,5, 259.00,4,NULL,  NULL,  16.01,1617.86),
(62,12,6,  14.00,5,NULL,  NULL,   1.00,1450.00),
-- ---- Profile 2 slips (2 items each: Honorarios + IRS) ----
(63,13,7, 500.00,1,NULL,NULL,NULL,NULL),
(64,13,8, 125.00,2,NULL,NULL,25.00,500.00),
(65,14,7, 350.00,1,NULL,NULL,NULL,NULL),
(66,14,8,  87.50,2,NULL,NULL,25.00,350.00),
(67,15,7, 600.00,1,NULL,NULL,NULL,NULL),
(68,15,8, 150.00,2,NULL,NULL,25.00,600.00),
(69,16,7, 250.00,1,NULL,NULL,NULL,NULL),
(70,16,8,  62.50,2,NULL,NULL,25.00,250.00),
(71,17,7, 150.00,1,NULL,NULL,NULL,NULL),
(72,17,8,  37.50,2,NULL,NULL,25.00,150.00),
(73,18,7, 450.00,1,NULL,NULL,NULL,NULL),
(74,18,8, 112.50,2,NULL,NULL,25.00,450.00),
(75,19,7, 500.00,1,NULL,NULL,NULL,NULL),
(76,19,8, 125.00,2,NULL,NULL,25.00,500.00),
(77,20,7, 350.00,1,NULL,NULL,NULL,NULL),
(78,20,8,  87.50,2,NULL,NULL,25.00,350.00),
(79,21,7, 200.00,1,NULL,NULL,NULL,NULL),
(80,21,8,  50.00,2,NULL,NULL,25.00,200.00),
(81,22,7, 400.00,1,NULL,NULL,NULL,NULL),
(82,22,8, 100.00,2,NULL,NULL,25.00,400.00),
(83,23,7, 550.00,1,NULL,NULL,NULL,NULL),
(84,23,8, 137.50,2,NULL,NULL,25.00,550.00),
(85,24,7, 480.00,1,NULL,NULL,NULL,NULL),
(86,24,8, 120.00,2,NULL,NULL,25.00,480.00);


-- ============================================================
-- 11. GROCERY CATEGORIES
-- ============================================================
-- Seeding grocery categories...

INSERT INTO [GroceryCategories] ([Id],[Name],[Color],[IsProtected]) VALUES
(1,'Produce',       '#4ade80',0),
(2,'Dairy',         '#60a5fa',0),
(3,'Meat & Fish',   '#f87171',0),
(4,'Bakery',        '#fb923c',0),
(5,'Beverages',     '#a78bfa',0),
(6,'Household',     '#94a3b8',0),
(7,'Personal Care', '#f472b6',0),
(8,'Other',         '#64748b',0);


-- ============================================================
-- 12. GROCERY CATEGORY RULES
-- ============================================================
-- Seeding grocery category rules...

INSERT INTO [GroceryCategoryRules] ([Id],[CategoryId],[Pattern],[Value]) VALUES
-- Produce
( 1,1,'FRUTA',  NULL),
( 2,1,'LEGUME', NULL),
( 3,1,'BANANA', NULL),
-- Dairy
( 4,2,'LEITE',  NULL),
( 5,2,'QUEIJO', NULL),
( 6,2,'IOGURTE',NULL),
-- Meat & Fish
( 7,3,'FRANGO', NULL),
( 8,3,'CARNE',  NULL),
( 9,3,'PEIXE',  NULL),
-- Bakery
(10,4,'PAO',    NULL),
(11,4,'BOLO',   NULL),
-- Beverages
(12,5,'AGUA',   NULL),
(13,5,'SUMO',   NULL),
-- Household
(14,6,'DETERGENTE',NULL),
-- Personal Care
(15,7,'SHAMPOO',NULL);


-- ============================================================
-- 13. GROCERY RECEIPTS  (2 stores × 12 months = 24)
--     Continente IDs 1–12  (mid-month, larger shop ~€50–90)
--     Pingo Doce IDs 13–24 (early-month, smaller shop ~€28–42)
-- ============================================================
-- Seeding grocery receipts...

INSERT INTO [GroceryReceipts] ([Id],[StoreName],[ReceiptDate],[Total],[Notes],[SourceFile],[PdfPath],[FileHash],[ImportedAt]) VALUES
-- Continente
( 1,'Continente','2025-04-12', 58.34,NULL,NULL,NULL,NULL,'2025-04-12T00:00:00'),
( 2,'Continente','2025-05-10', 62.15,NULL,NULL,NULL,NULL,'2025-05-10T00:00:00'),
( 3,'Continente','2025-06-07', 54.80,NULL,NULL,NULL,NULL,'2025-06-07T00:00:00'),
( 4,'Continente','2025-07-12', 71.20,NULL,NULL,NULL,NULL,'2025-07-12T00:00:00'),
( 5,'Continente','2025-08-09', 67.45,NULL,NULL,NULL,NULL,'2025-08-09T00:00:00'),
( 6,'Continente','2025-09-13', 55.60,NULL,NULL,NULL,NULL,'2025-09-13T00:00:00'),
( 7,'Continente','2025-10-11', 60.30,NULL,NULL,NULL,NULL,'2025-10-11T00:00:00'),
( 8,'Continente','2025-11-08', 63.90,NULL,NULL,NULL,NULL,'2025-11-08T00:00:00'),
( 9,'Continente','2025-12-13', 89.50,NULL,NULL,NULL,NULL,'2025-12-13T00:00:00'),
(10,'Continente','2026-01-10', 51.20,NULL,NULL,NULL,NULL,'2026-01-10T00:00:00'),
(11,'Continente','2026-02-07', 56.75,NULL,NULL,NULL,NULL,'2026-02-07T00:00:00'),
(12,'Continente','2026-03-14', 59.40,NULL,NULL,NULL,NULL,'2026-03-14T00:00:00'),
-- Pingo Doce
(13,'Pingo Doce','2025-04-05', 31.20,NULL,NULL,NULL,NULL,'2025-04-05T00:00:00'),
(14,'Pingo Doce','2025-05-03', 28.90,NULL,NULL,NULL,NULL,'2025-05-03T00:00:00'),
(15,'Pingo Doce','2025-06-14', 34.50,NULL,NULL,NULL,NULL,'2025-06-14T00:00:00'),
(16,'Pingo Doce','2025-07-05', 38.70,NULL,NULL,NULL,NULL,'2025-07-05T00:00:00'),
(17,'Pingo Doce','2025-08-02', 36.40,NULL,NULL,NULL,NULL,'2025-08-02T00:00:00'),
(18,'Pingo Doce','2025-09-06', 32.80,NULL,NULL,NULL,NULL,'2025-09-06T00:00:00'),
(19,'Pingo Doce','2025-10-04', 30.50,NULL,NULL,NULL,NULL,'2025-10-04T00:00:00'),
(20,'Pingo Doce','2025-11-01', 35.20,NULL,NULL,NULL,NULL,'2025-11-01T00:00:00'),
(21,'Pingo Doce','2025-12-06', 42.30,NULL,NULL,NULL,NULL,'2025-12-06T00:00:00'),
(22,'Pingo Doce','2026-01-03', 27.60,NULL,NULL,NULL,NULL,'2026-01-03T00:00:00'),
(23,'Pingo Doce','2026-02-14', 33.90,NULL,NULL,NULL,NULL,'2026-02-14T00:00:00'),
(24,'Pingo Doce','2026-03-07', 36.80,NULL,NULL,NULL,NULL,'2026-03-07T00:00:00');


-- ============================================================
-- 14. GROCERY ITEMS  (5 per Continente receipt = 60, IDs 1–60)
--                    (4 per Pingo Doce receipt  = 48, IDs 61–108)
--     All items rule-matched (CategorySetManually=0, Quantity=1)
-- ============================================================
-- Seeding grocery items...

INSERT INTO [GroceryItems] ([Id],[ReceiptId],[Description],[Amount],[Quantity],[CategoryId],[CategoryRuleId],[CategorySetManually],[IsExcluded]) VALUES
-- === Continente receipts (5 items each) ===
-- Receipt 1 — Apr 2025
( 1, 1,'FRANGO INTEIRO',    3.99,1,3, 7,0,0),
( 2, 1,'LEITE MIMOSA',      1.89,1,2, 4,0,0),
( 3, 1,'BANANA',            1.29,1,1, 3,0,0),
( 4, 1,'PAO DE FORMA',      1.49,1,4,10,0,0),
( 5, 1,'DETERGENTE ROUPA',  8.99,1,6,14,0,0),
-- Receipt 2 — May 2025
( 6, 2,'FRANGO INTEIRO',    4.29,1,3, 7,0,0),
( 7, 2,'LEITE MIMOSA',      1.89,1,2, 4,0,0),
( 8, 2,'BANANA',            1.39,1,1, 3,0,0),
( 9, 2,'PAO DE FORMA',      1.49,1,4,10,0,0),
(10, 2,'DETERGENTE ROUPA',  9.49,1,6,14,0,0),
-- Receipt 3 — Jun 2025
(11, 3,'FRANGO INTEIRO',    3.99,1,3, 7,0,0),
(12, 3,'LEITE MIMOSA',      1.99,1,2, 4,0,0),
(13, 3,'BANANA',            1.29,1,1, 3,0,0),
(14, 3,'PAO DE FORMA',      1.59,1,4,10,0,0),
(15, 3,'DETERGENTE ROUPA',  8.79,1,6,14,0,0),
-- Receipt 4 — Jul 2025
(16, 4,'FRANGO INTEIRO',    4.49,1,3, 7,0,0),
(17, 4,'LEITE MIMOSA',      1.99,1,2, 4,0,0),
(18, 4,'BANANA',            1.49,1,1, 3,0,0),
(19, 4,'PAO DE FORMA',      1.49,1,4,10,0,0),
(20, 4,'DETERGENTE ROUPA',  9.99,1,6,14,0,0),
-- Receipt 5 — Aug 2025
(21, 5,'FRANGO INTEIRO',    4.29,1,3, 7,0,0),
(22, 5,'LEITE MIMOSA',      2.09,1,2, 4,0,0),
(23, 5,'BANANA',            1.39,1,1, 3,0,0),
(24, 5,'PAO DE FORMA',      1.59,1,4,10,0,0),
(25, 5,'DETERGENTE ROUPA',  9.49,1,6,14,0,0),
-- Receipt 6 — Sep 2025
(26, 6,'FRANGO INTEIRO',    3.89,1,3, 7,0,0),
(27, 6,'LEITE MIMOSA',      1.89,1,2, 4,0,0),
(28, 6,'BANANA',            1.29,1,1, 3,0,0),
(29, 6,'PAO DE FORMA',      1.49,1,4,10,0,0),
(30, 6,'DETERGENTE ROUPA',  8.99,1,6,14,0,0),
-- Receipt 7 — Oct 2025
(31, 7,'FRANGO INTEIRO',    4.19,1,3, 7,0,0),
(32, 7,'LEITE MIMOSA',      1.99,1,2, 4,0,0),
(33, 7,'BANANA',            1.39,1,1, 3,0,0),
(34, 7,'PAO DE FORMA',      1.59,1,4,10,0,0),
(35, 7,'DETERGENTE ROUPA',  9.49,1,6,14,0,0),
-- Receipt 8 — Nov 2025
(36, 8,'FRANGO INTEIRO',    4.49,1,3, 7,0,0),
(37, 8,'LEITE MIMOSA',      2.09,1,2, 4,0,0),
(38, 8,'BANANA',            1.49,1,1, 3,0,0),
(39, 8,'PAO DE FORMA',      1.69,1,4,10,0,0),
(40, 8,'DETERGENTE ROUPA',  9.99,1,6,14,0,0),
-- Receipt 9 — Dec 2025
(41, 9,'FRANGO INTEIRO',    5.99,1,3, 7,0,0),
(42, 9,'LEITE MIMOSA',      2.19,1,2, 4,0,0),
(43, 9,'BANANA',            1.59,1,1, 3,0,0),
(44, 9,'PAO DE FORMA',      1.79,1,4,10,0,0),
(45, 9,'DETERGENTE ROUPA', 10.49,1,6,14,0,0),
-- Receipt 10 — Jan 2026
(46,10,'FRANGO INTEIRO',    3.79,1,3, 7,0,0),
(47,10,'LEITE MIMOSA',      1.89,1,2, 4,0,0),
(48,10,'BANANA',            1.19,1,1, 3,0,0),
(49,10,'PAO DE FORMA',      1.49,1,4,10,0,0),
(50,10,'DETERGENTE ROUPA',  8.49,1,6,14,0,0),
-- Receipt 11 — Feb 2026
(51,11,'FRANGO INTEIRO',    4.09,1,3, 7,0,0),
(52,11,'LEITE MIMOSA',      1.99,1,2, 4,0,0),
(53,11,'BANANA',            1.29,1,1, 3,0,0),
(54,11,'PAO DE FORMA',      1.59,1,4,10,0,0),
(55,11,'DETERGENTE ROUPA',  9.29,1,6,14,0,0),
-- Receipt 12 — Mar 2026
(56,12,'FRANGO INTEIRO',    4.19,1,3, 7,0,0),
(57,12,'LEITE MIMOSA',      1.99,1,2, 4,0,0),
(58,12,'BANANA',            1.39,1,1, 3,0,0),
(59,12,'PAO DE FORMA',      1.59,1,4,10,0,0),
(60,12,'DETERGENTE ROUPA',  9.49,1,6,14,0,0),
-- === Pingo Doce receipts (4 items each) ===
-- Receipt 13 — Apr 2025
(61,13,'IOGURTE NATURAL',   1.79,1,2, 6,0,0),
(62,13,'LEGUMES SALTEADOS', 2.49,1,1, 2,0,0),
(63,13,'AGUA 1.5L',         0.89,1,5,12,0,0),
(64,13,'QUEIJO FLAMENGO',   2.99,1,2, 5,0,0),
-- Receipt 14 — May 2025
(65,14,'IOGURTE NATURAL',   1.79,1,2, 6,0,0),
(66,14,'LEGUMES SALTEADOS', 2.29,1,1, 2,0,0),
(67,14,'AGUA 1.5L',         0.89,1,5,12,0,0),
(68,14,'QUEIJO FLAMENGO',   2.89,1,2, 5,0,0),
-- Receipt 15 — Jun 2025
(69,15,'IOGURTE NATURAL',   1.89,1,2, 6,0,0),
(70,15,'LEGUMES SALTEADOS', 2.59,1,1, 2,0,0),
(71,15,'AGUA 1.5L',         0.99,1,5,12,0,0),
(72,15,'QUEIJO FLAMENGO',   3.09,1,2, 5,0,0),
-- Receipt 16 — Jul 2025
(73,16,'IOGURTE NATURAL',   1.89,1,2, 6,0,0),
(74,16,'LEGUMES SALTEADOS', 2.49,1,1, 2,0,0),
(75,16,'AGUA 1.5L',         0.99,1,5,12,0,0),
(76,16,'QUEIJO FLAMENGO',   3.19,1,2, 5,0,0),
-- Receipt 17 — Aug 2025
(77,17,'IOGURTE NATURAL',   1.99,1,2, 6,0,0),
(78,17,'LEGUMES SALTEADOS', 2.59,1,1, 2,0,0),
(79,17,'AGUA 1.5L',         0.99,1,5,12,0,0),
(80,17,'QUEIJO FLAMENGO',   3.09,1,2, 5,0,0),
-- Receipt 18 — Sep 2025
(81,18,'IOGURTE NATURAL',   1.79,1,2, 6,0,0),
(82,18,'LEGUMES SALTEADOS', 2.39,1,1, 2,0,0),
(83,18,'AGUA 1.5L',         0.89,1,5,12,0,0),
(84,18,'QUEIJO FLAMENGO',   2.99,1,2, 5,0,0),
-- Receipt 19 — Oct 2025
(85,19,'IOGURTE NATURAL',   1.79,1,2, 6,0,0),
(86,19,'LEGUMES SALTEADOS', 2.29,1,1, 2,0,0),
(87,19,'AGUA 1.5L',         0.89,1,5,12,0,0),
(88,19,'QUEIJO FLAMENGO',   2.89,1,2, 5,0,0),
-- Receipt 20 — Nov 2025
(89,20,'IOGURTE NATURAL',   1.89,1,2, 6,0,0),
(90,20,'LEGUMES SALTEADOS', 2.59,1,1, 2,0,0),
(91,20,'AGUA 1.5L',         0.99,1,5,12,0,0),
(92,20,'QUEIJO FLAMENGO',   3.09,1,2, 5,0,0),
-- Receipt 21 — Dec 2025
(93,21,'IOGURTE NATURAL',   2.09,1,2, 6,0,0),
(94,21,'LEGUMES SALTEADOS', 2.79,1,1, 2,0,0),
(95,21,'AGUA 1.5L',         1.09,1,5,12,0,0),
(96,21,'QUEIJO FLAMENGO',   3.29,1,2, 5,0,0),
-- Receipt 22 — Jan 2026
( 97,22,'IOGURTE NATURAL',  1.69,1,2, 6,0,0),
( 98,22,'LEGUMES SALTEADOS',2.19,1,1, 2,0,0),
( 99,22,'AGUA 1.5L',        0.79,1,5,12,0,0),
(100,22,'QUEIJO FLAMENGO',  2.79,1,2, 5,0,0),
-- Receipt 23 — Feb 2026
(101,23,'IOGURTE NATURAL',  1.89,1,2, 6,0,0),
(102,23,'LEGUMES SALTEADOS',2.49,1,1, 2,0,0),
(103,23,'AGUA 1.5L',        0.89,1,5,12,0,0),
(104,23,'QUEIJO FLAMENGO',  3.09,1,2, 5,0,0),
-- Receipt 24 — Mar 2026
(105,24,'IOGURTE NATURAL',  1.99,1,2, 6,0,0),
(106,24,'LEGUMES SALTEADOS',2.59,1,1, 2,0,0),
(107,24,'AGUA 1.5L',        0.99,1,5,12,0,0),
(108,24,'QUEIJO FLAMENGO',  3.19,1,2, 5,0,0);


-- ============================================================
-- 15. MONTHLY STATEMENTS — Millennium BCP (3 statements, IDs 37–39)
--     Tests: large credit/debit values; statement 39 has intentionally
--     wrong ClosingBalance (real = 7843.51, stored = 7900.00) to trigger
--     the parse-warning banner in the UI.
-- ============================================================
-- Seeding Millennium BCP statements...

INSERT INTO [MonthlyStatements]
  ([Id],[Bank],[Account],[PeriodFrom],[PeriodTo],[Currency],[OpeningBalance],[ClosingBalance],[SourceFile],[PdfPath],[FileHash],[ImportedAt])
VALUES
(37,'MBcp','PT50 0035 0000 1111 2222 3','2025-04-01','2025-04-30','EUR',18000.00,20535.00,'mbcp_2025_04.pdf',NULL,NULL,'2025-05-03 10:00:00'),
(38,'MBcp','PT50 0035 0000 1111 2222 3','2025-05-01','2025-05-31','EUR',20535.00, 7440.00,'mbcp_2025_05.pdf',NULL,NULL,'2025-06-02 10:00:00'),
-- Statement 39: ClosingBalance stored as 7900.00 but transactions sum to 7843.51 → parse warning
(39,'MBcp','PT50 0035 0000 1111 2222 3','2025-06-01','2025-06-30','EUR', 7440.00, 7900.00,'mbcp_2025_06.pdf',NULL,NULL,'2025-07-02 10:00:00');


-- ============================================================
-- 16. TRANSACTIONS — Millennium BCP (6 per statement, IDs 313–330)
--     Stmt 37: credit €12 500 (large) + debit €10 200 (large)
--     Stmt 38: debit €15 000 car purchase
--     Stmt 39: transaction balances are correct; statement ClosingBalance is wrong
-- ============================================================
-- Seeding Millennium BCP transactions...

INSERT INTO [Transactions]
  ([Id],[StatementId],[DatePosting],[DateValue],[Description],[Amount],[Type],[Balance],[CategoryId],[CategoryRuleId],[CategorySetManually],[IsExcluded])
VALUES
-- === April 2025 (Stmt 37) — opening 18 000.00 ===
(313,37,'2025-04-03','2025-04-03','REEMBOLSO FINANCEIRO BANCO CTT',  12500.00,'credit',30500.00,10,NULL,1,0),
(314,37,'2025-04-08','2025-04-08','TRANSFERENCIA INVESTIMENTO FUNDO',10200.00,'debit', 20300.00, 9,  32,0,0),
(315,37,'2025-04-10','2025-04-10','AGEAS SEGUROS VIDA',                 120.00,'debit', 20180.00, 5,NULL,1,0),
(316,37,'2025-04-15','2025-04-15','RENDA GARAGEM ALVIDE',               150.00,'debit', 20030.00, 1,   1,0,0),
(317,37,'2025-04-20','2025-04-20','DIVIDENDO FUNDO CTT',                550.00,'credit',20580.00,10,NULL,0,0),
(318,37,'2025-04-27','2025-04-27','COMISSAO GESTAO CONTA',               45.00,'debit', 20535.00,NULL,NULL,0,0),
-- === May 2025 (Stmt 38) — opening 20 535.00; large debit: car purchase ===
(319,38,'2025-05-02','2025-05-02','PAGAMENTO AUTOMOVEL TOYOTA',       15000.00,'debit',  5535.00,NULL,NULL,0,0),
(320,38,'2025-05-06','2025-05-06','TRANSFERENCIA RECEBIDA SOCIO',      2500.00,'credit', 8035.00, 9,  32,0,0),
(321,38,'2025-05-10','2025-05-10','AGEAS SEGUROS VIDA',                  120.00,'debit', 7915.00, 5,NULL,1,0),
(322,38,'2025-05-15','2025-05-15','RENDA GARAGEM ALVIDE',                150.00,'debit', 7765.00, 1,   1,0,0),
(323,38,'2025-05-20','2025-05-20','COMISSAO GESTAO CONTA',                45.00,'debit', 7720.00,NULL,NULL,0,0),
(324,38,'2025-05-25','2025-05-25','JUROS CREDITO HABITACAO',             280.00,'debit', 7440.00, 1,NULL,1,0),
-- === June 2025 (Stmt 39) — opening 7 440.00; statement ClosingBalance intentionally wrong ===
(325,39,'2025-06-03','2025-06-03','SALARIO COMPLEMENTAR MBCP',          800.00,'credit', 8240.00, 8,  30,0,0),
(326,39,'2025-06-10','2025-06-10','AGEAS SEGUROS VIDA',                  120.00,'debit', 8120.00, 5,NULL,1,0),
(327,39,'2025-06-15','2025-06-15','RENDA GARAGEM ALVIDE',                150.00,'debit', 7970.00, 1,   1,0,0),
(328,39,'2025-06-20','2025-06-20','COMISSAO GESTAO CONTA',                45.00,'debit', 7925.00,NULL,NULL,0,0),
(329,39,'2025-06-24','2025-06-24','SUPERMERCADO COMPRA ONLINE',           67.50,'debit', 7857.50, 2,NULL,1,0),
(330,39,'2025-06-28','2025-06-28','NETFLIX PAGAMENTO',                    13.99,'debit', 7843.51, 6,  22,0,0);


-- ============================================================
-- 17. SALARY PROFILE — Tech Lead Role (ID 3)
-- ============================================================
-- Seeding Tech Lead salary profile...

INSERT INTO [SalaryProfiles] ([Id],[Name],[Description],[HourlyRateFormula]) VALUES
(3,'Tech Lead Role','Senior engineering role with performance bonuses','days');


-- ============================================================
-- 18. SALARY ITEM CATEGORIES — Profile 3 (IDs 9–13)
-- ============================================================
-- Seeding Tech Lead salary item categories...

INSERT INTO [SalaryItemCategories] ([Id],[SalaryProfileId],[Name],[Color],[ItemType],[IsProtected]) VALUES
( 9,3,'Vencimento Base',  '#10b981','income',   0),
(10,3,'Subsidio Refeicao','#34d399','income',   0),
(11,3,'Bonus / Subsidio', '#6ee7b7','income',   0),
(12,3,'Seguranca Social', '#f87171','deduction',0),
(13,3,'IRS Retencao',     '#fca5a5','tax',      0);


-- ============================================================
-- 19. SALARY SLIPS — Profile 3 (6 slips, IDs 25–30)
--     Regular:  gross = 5 000, net = 3 800
--     Slip 28 (Jul): gross = 8 500, net = 6 500 (holiday bonus €3 500)
--     Slip 30 (Sep): gross stored as 5 000 but income items sum to 4 600
--                    → triggers salary parse warning in UI
-- ============================================================
-- Seeding Tech Lead salary slips...

INSERT INTO [SalarySlips]
  ([Id],[SalaryProfileId],[Period],[GrossAmount],[NetAmount],[Notes],[SourceFile],[PdfPath],[FileHash],[ImportedAt],[BaseAmount],[HoursWorked],[HourlyRate],[TotalEspecie])
VALUES
(25,3,'2025-04-01',5000.00,3800.00,NULL,
 'techlead_2025_04.pdf',NULL,NULL,'2025-05-03 10:00:00',4780.00,176.00,27.16,220.00),
(26,3,'2025-05-01',5000.00,3800.00,NULL,
 'techlead_2025_05.pdf',NULL,NULL,'2025-06-02 10:00:00',4780.00,176.00,27.16,220.00),
(27,3,'2025-06-01',5000.00,3800.00,NULL,
 'techlead_2025_06.pdf',NULL,NULL,'2025-07-02 10:00:00',4780.00,176.00,27.16,220.00),
(28,3,'2025-07-01',8500.00,6500.00,'Demo note: includes holiday bonus €3 500',
 'techlead_2025_07.pdf',NULL,NULL,'2025-08-02 10:00:00',8280.00,176.00,27.16,220.00),
(29,3,'2025-08-01',5000.00,3800.00,NULL,
 'techlead_2025_08.pdf',NULL,NULL,'2025-09-03 10:00:00',4780.00,176.00,27.16,220.00),
(30,3,'2025-09-01',5000.00,3800.00,'Demo note: parse warning — income items sum (4600) does not match gross (5000)',
 'techlead_2025_09.pdf',NULL,NULL,'2025-10-02 10:00:00',4780.00,176.00,27.16,220.00);


-- ============================================================
-- 20. SALARY LINE ITEMS — Profile 3 (IDs 87–111)
--     Regular slips (4 items): Vencimento Base + Subsidio Refeicao
--                              - Seguranca Social - IRS
--     Slip 28 (5 items): adds Bonus / Subsidio line (€3 500 holiday bonus)
--     Slip 30 (4 items): Vencimento Base intentionally low (€4 380 not €4 780)
--                        so income sum = 4 600 ≠ gross 5 000 → parse warning
-- ============================================================
-- Seeding Tech Lead salary line items...

INSERT INTO [SalaryLineItems]
  ([Id],[SalarySlipId],[SalaryItemCategoryId],[Amount],[SortOrder],[Quantity],[UnitValue],[Percentage],[IncidenciaBase])
VALUES
-- ---- Slip 25 (Apr 2025) ----
( 87,25, 9,4780.00,1,NULL,  NULL,  NULL,    NULL),
( 88,25,10, 220.00,2,22.00,10.00,  NULL,    NULL),
( 89,25,12, 550.00,3,NULL,  NULL,  11.00,5000.00),
( 90,25,13, 650.00,4,NULL,  NULL,  13.00,5000.00),
-- ---- Slip 26 (May 2025) ----
( 91,26, 9,4780.00,1,NULL,  NULL,  NULL,    NULL),
( 92,26,10, 220.00,2,22.00,10.00,  NULL,    NULL),
( 93,26,12, 550.00,3,NULL,  NULL,  11.00,5000.00),
( 94,26,13, 650.00,4,NULL,  NULL,  13.00,5000.00),
-- ---- Slip 27 (Jun 2025) ----
( 95,27, 9,4780.00,1,NULL,  NULL,  NULL,    NULL),
( 96,27,10, 220.00,2,22.00,10.00,  NULL,    NULL),
( 97,27,12, 550.00,3,NULL,  NULL,  11.00,5000.00),
( 98,27,13, 650.00,4,NULL,  NULL,  13.00,5000.00),
-- ---- Slip 28 (Jul 2025 — holiday bonus €3 500) ----
( 99,28, 9,4780.00,1,NULL,  NULL,  NULL,    NULL),
(100,28,10, 220.00,2,22.00,10.00,  NULL,    NULL),
(101,28,11,3500.00,3,NULL,  NULL,  NULL,    NULL),
(102,28,12, 935.00,4,NULL,  NULL,  11.00,8500.00),
(103,28,13,1065.00,5,NULL,  NULL,  12.53,8500.00),
-- ---- Slip 29 (Aug 2025) ----
(104,29, 9,4780.00,1,NULL,  NULL,  NULL,    NULL),
(105,29,10, 220.00,2,22.00,10.00,  NULL,    NULL),
(106,29,12, 550.00,3,NULL,  NULL,  11.00,5000.00),
(107,29,13, 650.00,4,NULL,  NULL,  13.00,5000.00),
-- ---- Slip 30 (Sep 2025 — parse warning: income sums to 4 600 not 5 000) ----
(108,30, 9,4380.00,1,NULL,  NULL,  NULL,    NULL),
(109,30,10, 220.00,2,22.00,10.00,  NULL,    NULL),
(110,30,12, 550.00,3,NULL,  NULL,  11.00,5000.00),
(111,30,13, 650.00,4,NULL,  NULL,  13.00,5000.00);


-- ============================================================
-- 21. GROCERY RECEIPTS — Mercadona (3 receipts, IDs 25–27)
--     Third store whose items share category IDs with Continente and
--     Pingo Doce, exercising UI category-deduplication when totalling
--     spend across stores.
-- ============================================================
-- Seeding Mercadona grocery receipts...

INSERT INTO [GroceryReceipts]
  ([Id],[StoreName],[ReceiptDate],[Total],[Notes],[SourceFile],[PdfPath],[FileHash],[ImportedAt])
VALUES
(25,'Mercadona','2025-04-18',167.16,NULL,NULL,NULL,NULL,'2025-04-18T00:00:00'),
(26,'Mercadona','2025-06-21', 18.54,NULL,NULL,NULL,NULL,'2025-06-21T00:00:00'),
(27,'Mercadona','2025-09-20', 15.25,NULL,NULL,NULL,NULL,'2025-09-20T00:00:00');


-- ============================================================
-- 22. GROCERY ITEMS — Mercadona (5 per receipt, IDs 109–123)
--     Receipt 25: VINHO PREMIUM €150.00 × 1 — large unit price
--     Receipt 26: AGUA MINERAL €0.49 × 12 — high quantity
--     All three receipts assign items to categories 1–7 (same IDs used
--     by Continente and Pingo Doce) to exercise deduplication.
-- ============================================================
-- Seeding Mercadona grocery items...

INSERT INTO [GroceryItems]
  ([Id],[ReceiptId],[Description],[Amount],[Quantity],[CategoryId],[CategoryRuleId],[CategorySetManually],[IsExcluded])
VALUES
-- Receipt 25 — Apr 2025 (large unit price)
(109,25,'VINHO PREMIUM COLHEITA',150.00,1,8,NULL,1,0),
(110,25,'FRANGO ASSADO',           6.99,1,3,   7,0,0),
(111,25,'LEITE INTEIRO',           1.89,1,2,   4,0,0),
(112,25,'LEGUMES MISTURADOS',      3.29,1,1,   2,0,0),
(113,25,'QUEIJO CURADO',           4.99,1,2,   5,0,0),
-- Receipt 26 — Jun 2025 (high quantity × 12)
(114,26,'AGUA MINERAL 0.5L',       0.49,12,5,  12,0,0),
(115,26,'FRANGO PERNA KG',         5.49, 1,3,   7,0,0),
(116,26,'IOGURTE NATURAL',         1.99, 1,2,   6,0,0),
(117,26,'BANANA CAVENDISH',        1.39, 1,1,   3,0,0),
(118,26,'DETERGENTE LOICA',        3.79, 1,6,  14,0,0),
-- Receipt 27 — Sep 2025
(119,27,'CARNE PICADA BOI',        4.49,1,3,   8,0,0),
(120,27,'LEITE DESNATADO',         1.79,1,2,   4,0,0),
(121,27,'BANANA PACK',             2.19,1,1,   3,0,0),
(122,27,'PAO INTEGRAL',            2.49,1,4,  10,0,0),
(123,27,'SHAMPOO ELVIVE',          4.29,1,7,  15,0,0);


-- ============================================================
-- 23. EXCLUDED RECORDS
--     Transactions (5): mix of rule-matched and uncategorised
--       148, 282 — no category, no rule (Holmes Place; DigitalOcean)
--       174, 181, 245 — category + rule assigned (restaurant; Amazon; Steam)
--     Grocery items (3): rule-matched items excluded to test the
--       "categorised but excluded" combination
-- ============================================================
-- Marking excluded transactions and grocery items...

UPDATE [Transactions]
SET IsExcluded = 1
WHERE Id IN (148, 174, 181, 245, 282);

UPDATE [GroceryItems]
SET IsExcluded = 1
WHERE Id IN (5, 25, 40);

-- ============================================================
-- 24. CGD BANK — business / consulting account (IDs 40–51, txns 331–348)
--     Provides bank deposits that correspond to the two profiles that
--     otherwise have no matching transactions:
--       · Side Consulting (profile 2): HONORARIOS CONSULTORIA each month
--       · Tech Lead Role  (profile 3): VENCIMENTO TECH LEAD EMPRESA Apr–Sep
--     Opening balance €2 000 grows purely from credits (receiving account).
-- ============================================================
-- Seeding CGD (business account) statements...

INSERT INTO [MonthlyStatements]
  ([Id],[Bank],[Account],[PeriodFrom],[PeriodTo],[Currency],[OpeningBalance],[ClosingBalance],[SourceFile],[PdfPath],[FileHash],[ImportedAt])
VALUES
-- Apr–Sep 2025: consulting + tech lead salary (2 credits each)
(40,'CGD','PT50 0130 0000 4444 5555 6','2025-04-01','2025-04-30','EUR', 2000.00, 6175.00,'cgd_2025_04.pdf',NULL,NULL,'2025-05-03 10:00:00'),
(41,'CGD','PT50 0130 0000 4444 5555 6','2025-05-01','2025-05-31','EUR', 6175.00,10237.50,'cgd_2025_05.pdf',NULL,NULL,'2025-06-02 10:00:00'),
(42,'CGD','PT50 0130 0000 4444 5555 6','2025-06-01','2025-06-30','EUR',10237.50,14487.50,'cgd_2025_06.pdf',NULL,NULL,'2025-07-02 10:00:00'),
(43,'CGD','PT50 0130 0000 4444 5555 6','2025-07-01','2025-07-31','EUR',14487.50,21175.00,'cgd_2025_07.pdf',NULL,NULL,'2025-08-02 10:00:00'),
(44,'CGD','PT50 0130 0000 4444 5555 6','2025-08-01','2025-08-31','EUR',21175.00,25087.50,'cgd_2025_08.pdf',NULL,NULL,'2025-09-03 10:00:00'),
(45,'CGD','PT50 0130 0000 4444 5555 6','2025-09-01','2025-09-30','EUR',25087.50,29225.00,'cgd_2025_09.pdf',NULL,NULL,'2025-10-02 10:00:00'),
-- Oct 2025–Mar 2026: consulting only (1 credit each)
(46,'CGD','PT50 0130 0000 4444 5555 6','2025-10-01','2025-10-31','EUR',29225.00,29600.00,'cgd_2025_10.pdf',NULL,NULL,'2025-11-03 10:00:00'),
(47,'CGD','PT50 0130 0000 4444 5555 6','2025-11-01','2025-11-30','EUR',29600.00,29862.50,'cgd_2025_11.pdf',NULL,NULL,'2025-12-02 10:00:00'),
(48,'CGD','PT50 0130 0000 4444 5555 6','2025-12-01','2025-12-31','EUR',29862.50,30012.50,'cgd_2025_12.pdf',NULL,NULL,'2026-01-03 10:00:00'),
(49,'CGD','PT50 0130 0000 4444 5555 6','2026-01-01','2026-01-31','EUR',30012.50,30312.50,'cgd_2026_01.pdf',NULL,NULL,'2026-02-02 10:00:00'),
(50,'CGD','PT50 0130 0000 4444 5555 6','2026-02-01','2026-02-28','EUR',30312.50,30725.00,'cgd_2026_02.pdf',NULL,NULL,'2026-03-03 10:00:00'),
(51,'CGD','PT50 0130 0000 4444 5555 6','2026-03-01','2026-03-31','EUR',30725.00,31085.00,'cgd_2026_03.pdf',NULL,NULL,'2026-04-02 10:00:00');


-- Seeding CGD transactions...

INSERT INTO [Transactions]
  ([Id],[StatementId],[DatePosting],[DateValue],[Description],[Amount],[Type],[Balance],[CategoryId],[CategoryRuleId],[CategorySetManually],[IsExcluded])
VALUES
-- === April 2025 (Stmt 40) — opening 2 000.00 ===
(331,40,'2025-04-28','2025-04-28','HONORARIOS CONSULTORIA XYZ LDA',  375.00,'credit', 2375.00,8,NULL,1,0),
(332,40,'2025-04-30','2025-04-30','VENCIMENTO TECH LEAD EMPRESA',    3800.00,'credit', 6175.00,8,  31,0,0),
-- === May 2025 (Stmt 41) — opening 6 175.00 ===
(333,41,'2025-05-28','2025-05-28','HONORARIOS CONSULTORIA XYZ LDA',  262.50,'credit', 6437.50,8,NULL,1,0),
(334,41,'2025-05-30','2025-05-30','VENCIMENTO TECH LEAD EMPRESA',    3800.00,'credit',10237.50,8,  31,0,0),
-- === June 2025 (Stmt 42) — opening 10 237.50 ===
(335,42,'2025-06-28','2025-06-28','HONORARIOS CONSULTORIA XYZ LDA',  450.00,'credit',10687.50,8,NULL,1,0),
(336,42,'2025-06-30','2025-06-30','VENCIMENTO TECH LEAD EMPRESA',    3800.00,'credit',14487.50,8,  31,0,0),
-- === July 2025 (Stmt 43) — opening 14 487.50; bonus month ===
(337,43,'2025-07-28','2025-07-28','HONORARIOS CONSULTORIA XYZ LDA',  187.50,'credit',14675.00,8,NULL,1,0),
(338,43,'2025-07-30','2025-07-30','VENCIMENTO TECH LEAD EMPRESA INC SUBSIDIO FERIAS',6500.00,'credit',21175.00,8,31,0,0),
-- === August 2025 (Stmt 44) — opening 21 175.00 ===
(339,44,'2025-08-28','2025-08-28','HONORARIOS CONSULTORIA XYZ LDA',  112.50,'credit',21287.50,8,NULL,1,0),
(340,44,'2025-08-30','2025-08-30','VENCIMENTO TECH LEAD EMPRESA',    3800.00,'credit',25087.50,8,  31,0,0),
-- === September 2025 (Stmt 45) — opening 25 087.50 ===
(341,45,'2025-09-28','2025-09-28','HONORARIOS CONSULTORIA XYZ LDA',  337.50,'credit',25425.00,8,NULL,1,0),
(342,45,'2025-09-30','2025-09-30','VENCIMENTO TECH LEAD EMPRESA',    3800.00,'credit',29225.00,8,  31,0,0),
-- === October 2025 (Stmt 46) — consulting only from here ===
(343,46,'2025-10-28','2025-10-28','HONORARIOS CONSULTORIA XYZ LDA',  375.00,'credit',29600.00,8,NULL,1,0),
-- === November 2025 (Stmt 47) ===
(344,47,'2025-11-28','2025-11-28','HONORARIOS CONSULTORIA XYZ LDA',  262.50,'credit',29862.50,8,NULL,1,0),
-- === December 2025 (Stmt 48) ===
(345,48,'2025-12-28','2025-12-28','HONORARIOS CONSULTORIA XYZ LDA',  150.00,'credit',30012.50,8,NULL,1,0),
-- === January 2026 (Stmt 49) ===
(346,49,'2026-01-28','2026-01-28','HONORARIOS CONSULTORIA XYZ LDA',  300.00,'credit',30312.50,8,NULL,1,0),
-- === February 2026 (Stmt 50) ===
(347,50,'2026-02-28','2026-02-28','HONORARIOS CONSULTORIA XYZ LDA',  412.50,'credit',30725.00,8,NULL,1,0),
-- === March 2026 (Stmt 51) ===
(348,51,'2026-03-28','2026-03-28','HONORARIOS CONSULTORIA XYZ LDA',  360.00,'credit',31085.00,8,NULL,1,0);


-- ============================================================
-- 25. DATA FIXES
--     MBcp Jun 2025 had a stray "SALARIO" label on a credit that has no
--     matching salary slip. Recategorise as Other so it does not inflate
--     the Salary income line in analytics.
-- ============================================================
-- Applying data fixes...

UPDATE [Transactions]
SET Description      = 'DIVIDENDO COMPLEMENTAR MBCP',
    CategoryId       = 10,
    CategoryRuleId   = NULL,
    CategorySetManually = 1
WHERE Id = 325;

-- ============================================================
-- 26. CATEGORY with an accented first letter, and its rule
--     'Ótica' sorts among the O's (DISPLAY_ORDER), not after 'Z'.
-- ============================================================

INSERT INTO [Categories] ([Id],[Name],[Color],[IsProtected]) VALUES
(11,'Ótica','#0ea5e9',0);

INSERT INTO [CategoryRules] ([Id],[CategoryId],[Pattern],[Value]) VALUES
(34,11,'OTICA',NULL);


-- ============================================================
-- 27. TRADE REPUBLIC (statements 52–63) and MEAL CARD (64–75)
--     Trade Republic: a monthly transfer in from BPI [internal], a monthly
--       'Savings plan execution' ETF buy (excluded, no category; mirrored as
--       lots in section 29), interest, card payments, and the Deel payouts
--       of the micro1 slips (section 31), two of them in March 2026.
--     MEAL CARD: as the meal card text import stores it: no account, no
--       PDF, every row's Balance 0, only the statement balances chained.
-- ============================================================

INSERT INTO [MonthlyStatements]
  ([Id],[Bank],[Account],[PeriodFrom],[PeriodTo],[Currency],[OpeningBalance],[ClosingBalance],[SourceFile],[PdfPath],[FileHash],[ImportedAt])
VALUES
(52,'TRADE REPUBLIC','PT50 0000 0000 7777 8888 9','2025-04-01','2025-04-30','EUR',500.00,550.83,'trade_republic_2025_04.pdf',NULL,NULL,'2025-05-02 10:00:00'),
(53,'TRADE REPUBLIC','PT50 0000 0000 7777 8888 9','2025-05-01','2025-05-31','EUR',550.83,601.75,'trade_republic_2025_05.pdf',NULL,NULL,'2025-06-02 10:00:00'),
(54,'TRADE REPUBLIC','PT50 0000 0000 7777 8888 9','2025-06-01','2025-06-30','EUR',601.75,523.75,'trade_republic_2025_06.pdf',NULL,NULL,'2025-07-02 10:00:00'),
(55,'TRADE REPUBLIC','PT50 0000 0000 7777 8888 9','2025-07-01','2025-07-31','EUR',523.75,574.62,'trade_republic_2025_07.pdf',NULL,NULL,'2025-08-02 10:00:00'),
(56,'TRADE REPUBLIC','PT50 0000 0000 7777 8888 9','2025-08-01','2025-08-31','EUR',574.62,625.58,'trade_republic_2025_08.pdf',NULL,NULL,'2025-09-02 10:00:00'),
(57,'TRADE REPUBLIC','PT50 0000 0000 7777 8888 9','2025-09-01','2025-09-30','EUR',625.58,676.62,'trade_republic_2025_09.pdf',NULL,NULL,'2025-10-02 10:00:00'),
(58,'TRADE REPUBLIC','PT50 0000 0000 7777 8888 9','2025-10-01','2025-10-31','EUR',676.62,720.45,'trade_republic_2025_10.pdf',NULL,NULL,'2025-11-02 10:00:00'),
(59,'TRADE REPUBLIC','PT50 0000 0000 7777 8888 9','2025-11-01','2025-11-30','EUR',720.45,771.65,'trade_republic_2025_11.pdf',NULL,NULL,'2025-12-02 10:00:00'),
(60,'TRADE REPUBLIC','PT50 0000 0000 7777 8888 9','2025-12-01','2025-12-31','EUR',771.65,822.94,'trade_republic_2025_12.pdf',NULL,NULL,'2026-01-02 10:00:00'),
(61,'TRADE REPUBLIC','PT50 0000 0000 7777 8888 9','2026-01-01','2026-01-31','EUR',822.94,2988.73,'trade_republic_2026_01.pdf',NULL,NULL,'2026-02-02 10:00:00'),
(62,'TRADE REPUBLIC','PT50 0000 0000 7777 8888 9','2026-02-01','2026-02-28','EUR',2988.73,5146.24,'trade_republic_2026_02.pdf',NULL,NULL,'2026-03-02 10:00:00'),
(63,'TRADE REPUBLIC','PT50 0000 0000 7777 8888 9','2026-03-01','2026-03-31','EUR',5146.24,7355.79,'trade_republic_2026_03.pdf',NULL,NULL,'2026-04-02 10:00:00'),
(64,'MEAL CARD','','2025-04-01','2025-04-30','EUR',42.15,42.15,'meal-card-text-import',NULL,NULL,'2025-05-04 10:00:00'),
(65,'MEAL CARD','','2025-05-01','2025-05-31','EUR',42.15,45.15,'meal-card-text-import',NULL,NULL,'2025-06-04 10:00:00'),
(66,'MEAL CARD','','2025-06-01','2025-06-30','EUR',45.15,51.15,'meal-card-text-import',NULL,NULL,'2025-07-04 10:00:00'),
(67,'MEAL CARD','','2025-07-01','2025-07-31','EUR',51.15,51.15,'meal-card-text-import',NULL,NULL,'2025-08-04 10:00:00'),
(68,'MEAL CARD','','2025-08-01','2025-08-31','EUR',51.15,54.15,'meal-card-text-import',NULL,NULL,'2025-09-04 10:00:00'),
(69,'MEAL CARD','','2025-09-01','2025-09-30','EUR',54.15,60.15,'meal-card-text-import',NULL,NULL,'2025-10-04 10:00:00'),
(70,'MEAL CARD','','2025-10-01','2025-10-31','EUR',60.15,60.15,'meal-card-text-import',NULL,NULL,'2025-11-04 10:00:00'),
(71,'MEAL CARD','','2025-11-01','2025-11-30','EUR',60.15,63.15,'meal-card-text-import',NULL,NULL,'2025-12-04 10:00:00'),
(72,'MEAL CARD','','2025-12-01','2025-12-31','EUR',63.15,69.15,'meal-card-text-import',NULL,NULL,'2026-01-04 10:00:00'),
(73,'MEAL CARD','','2026-01-01','2026-01-31','EUR',69.15,69.15,'meal-card-text-import',NULL,NULL,'2026-02-04 10:00:00'),
(74,'MEAL CARD','','2026-02-01','2026-02-28','EUR',69.15,72.15,'meal-card-text-import',NULL,NULL,'2026-03-04 10:00:00'),
(75,'MEAL CARD','','2026-03-01','2026-03-31','EUR',72.15,78.15,'meal-card-text-import',NULL,NULL,'2026-04-04 10:00:00');

INSERT INTO [Transactions]
  ([Id],[StatementId],[DatePosting],[DateValue],[Description],[Amount],[Type],[Balance],[CategoryId],[CategoryRuleId],[CategorySetManually],[IsExcluded])
VALUES
(349,52,'2025-04-01','2025-04-01','Transfer Incoming transfer from Demo Holder (PT50 0010 0000 5678 9012 1)',200.00,'credit',700.00,9,NULL,1,1),
(350,52,'2025-04-02','2025-04-02','Trade Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All- World UCITS ETF (USD) Accumulating, quantity: 1.266892',150.00,'debit',550.00,NULL,NULL,0,1),
(351,52,'2025-04-30','2025-04-30','Interest Your interest payment',0.83,'credit',550.83,10,NULL,1,0),
(352,53,'2025-05-01','2025-05-01','Transfer Incoming transfer from Demo Holder (PT50 0010 0000 5678 9012 1)',200.00,'credit',750.83,9,NULL,1,1),
(353,53,'2025-05-02','2025-05-02','Trade Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All- World UCITS ETF (USD) Accumulating, quantity: 1.248439',150.00,'debit',600.83,NULL,NULL,0,1),
(354,53,'2025-05-31','2025-05-31','Interest Your interest payment',0.92,'credit',601.75,10,NULL,1,0),
(355,54,'2025-06-01','2025-06-01','Transfer Incoming transfer from Demo Holder (PT50 0010 0000 5678 9012 1)',200.00,'credit',801.75,9,NULL,1,1),
(356,54,'2025-06-02','2025-06-02','Trade Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All- World UCITS ETF (USD) Accumulating, quantity: 1.230517',150.00,'debit',651.75,NULL,NULL,0,1),
(357,54,'2025-06-14','2025-06-14','OTICAS LUX Card Transaction',129.00,'debit',522.75,11,34,0,0),
(358,54,'2025-06-30','2025-06-30','Interest Your interest payment',1.00,'credit',523.75,10,NULL,1,0),
(359,55,'2025-07-01','2025-07-01','Transfer Incoming transfer from Demo Holder (PT50 0010 0000 5678 9012 1)',200.00,'credit',723.75,9,NULL,1,1),
(360,55,'2025-07-02','2025-07-02','Trade Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All- World UCITS ETF (USD) Accumulating, quantity: 1.206273',150.00,'debit',573.75,NULL,NULL,0,1),
(361,55,'2025-07-31','2025-07-31','Interest Your interest payment',0.87,'credit',574.62,10,NULL,1,0),
(362,56,'2025-08-01','2025-08-01','Transfer Incoming transfer from Demo Holder (PT50 0010 0000 5678 9012 1)',200.00,'credit',774.62,9,NULL,1,1),
(363,56,'2025-08-02','2025-08-02','Trade Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All- World UCITS ETF (USD) Accumulating, quantity: 1.218522',150.00,'debit',624.62,NULL,NULL,0,1),
(364,56,'2025-08-31','2025-08-31','Interest Your interest payment',0.96,'credit',625.58,10,NULL,1,0),
(365,57,'2025-09-01','2025-09-01','Transfer Incoming transfer from Demo Holder (PT50 0010 0000 5678 9012 1)',200.00,'credit',825.58,9,NULL,1,1),
(366,57,'2025-09-02','2025-09-02','Trade Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All- World UCITS ETF (USD) Accumulating, quantity: 1.182965',150.00,'debit',675.58,NULL,NULL,0,1),
(367,57,'2025-09-30','2025-09-30','Interest Your interest payment',1.04,'credit',676.62,10,NULL,1,0),
(368,58,'2025-10-01','2025-10-01','Transfer Incoming transfer from Demo Holder (PT50 0010 0000 5678 9012 1)',200.00,'credit',876.62,9,NULL,1,1),
(369,58,'2025-10-02','2025-10-02','Trade Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All- World UCITS ETF (USD) Accumulating, quantity: 1.158749',150.00,'debit',726.62,NULL,NULL,0,1),
(370,58,'2025-10-09','2025-10-09','MINI MERCADO Card Transaction',7.30,'debit',719.32,NULL,NULL,0,0),
(371,58,'2025-10-31','2025-10-31','Interest Your interest payment',1.13,'credit',720.45,10,NULL,1,0),
(372,59,'2025-11-01','2025-11-01','Transfer Incoming transfer from Demo Holder (PT50 0010 0000 5678 9012 1)',200.00,'credit',920.45,9,NULL,1,1),
(373,59,'2025-11-02','2025-11-02','Trade Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All- World UCITS ETF (USD) Accumulating, quantity: 1.170047',150.00,'debit',770.45,NULL,NULL,0,1),
(374,59,'2025-11-30','2025-11-30','Interest Your interest payment',1.20,'credit',771.65,10,NULL,1,0),
(375,60,'2025-12-01','2025-12-01','Transfer Incoming transfer from Demo Holder (PT50 0010 0000 5678 9012 1)',200.00,'credit',971.65,9,NULL,1,1),
(376,60,'2025-12-02','2025-12-02','Trade Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All- World UCITS ETF (USD) Accumulating, quantity: 1.139818',150.00,'debit',821.65,NULL,NULL,0,1),
(377,60,'2025-12-31','2025-12-31','Interest Your interest payment',1.29,'credit',822.94,10,NULL,1,0),
(378,61,'2026-01-01','2026-01-01','Transfer Incoming transfer from Demo Holder (PT50 0010 0000 5678 9012 1)',200.00,'credit',1022.94,9,NULL,1,1),
(379,61,'2026-01-02','2026-01-02','Trade Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All- World UCITS ETF (USD) Accumulating, quantity: 1.127396',150.00,'debit',872.94,NULL,NULL,0,1),
(380,61,'2026-01-27','2026-01-27','Transfer Incoming transfer from Deel Inc.',2114.42,'credit',2987.36,8,NULL,1,0),
(381,61,'2026-01-31','2026-01-31','Interest Your interest payment',1.37,'credit',2988.73,10,NULL,1,0),
(382,62,'2026-02-01','2026-02-01','Transfer Incoming transfer from Demo Holder (PT50 0010 0000 5678 9012 1)',200.00,'credit',3188.73,9,NULL,1,1),
(383,62,'2026-02-02','2026-02-02','Trade Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All- World UCITS ETF (USD) Accumulating, quantity: 1.147666',150.00,'debit',3038.73,NULL,NULL,0,1),
(384,62,'2026-02-19','2026-02-19','OTICAS LUX Card Transaction',45.00,'debit',2993.73,11,34,0,0),
(385,62,'2026-02-26','2026-02-26','Transfer Incoming transfer from Deel Inc.',2147.53,'credit',5141.26,8,NULL,1,0),
(386,62,'2026-02-28','2026-02-28','Interest Your interest payment',4.98,'credit',5146.24,10,NULL,1,0),
(387,63,'2026-03-01','2026-03-01','Transfer Incoming transfer from Demo Holder (PT50 0010 0000 5678 9012 1)',200.00,'credit',5346.24,9,NULL,1,1),
(388,63,'2026-03-02','2026-03-02','Trade Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All- World UCITS ETF (USD) Accumulating, quantity: 1.117318',150.00,'debit',5196.24,NULL,NULL,0,1),
(389,63,'2026-03-06','2026-03-06','MINI MERCADO Card Transaction',12.45,'debit',5183.79,NULL,NULL,0,0),
(390,63,'2026-03-13','2026-03-13','Transfer Incoming transfer from Deel Inc.',1054.51,'credit',6238.30,8,NULL,1,0),
(391,63,'2026-03-27','2026-03-27','Transfer Incoming transfer from Deel Inc.',1108.91,'credit',7347.21,8,NULL,1,0),
(392,63,'2026-03-31','2026-03-31','Interest Your interest payment',8.58,'credit',7355.79,10,NULL,1,0),
(393,64,'2025-04-03','2025-04-03','CARREGAMENTO EMPRESA ABC LDA',167.86,'credit',0.00,8,NULL,1,0),
(394,64,'2025-04-08','2025-04-08','RESTAURANTE O PATEO',11.40,'debit',0.00,2,8,0,0),
(395,64,'2025-04-16','2025-04-16','PINGO DOCE ALFAMA',23.85,'debit',0.00,2,4,0,0),
(396,64,'2025-04-24','2025-04-24','PADARIA CENTRAL',6.70,'debit',0.00,NULL,NULL,0,0),
(397,64,'2025-04-28','2025-04-28','CONTINENTE BOM DIA',125.91,'debit',0.00,2,3,0,0),
(398,65,'2025-05-03','2025-05-03','CARREGAMENTO EMPRESA ABC LDA',167.86,'credit',0.00,8,NULL,1,0),
(399,65,'2025-05-07','2025-05-07','RESTAURANTE O PATEO',12.10,'debit',0.00,2,8,0,0),
(400,65,'2025-05-15','2025-05-15','PINGO DOCE ALFAMA',31.20,'debit',0.00,2,4,0,0),
(401,65,'2025-05-22','2025-05-22','PADARIA CENTRAL',5.90,'debit',0.00,NULL,NULL,0,0),
(402,65,'2025-05-28','2025-05-28','CONTINENTE BOM DIA',115.66,'debit',0.00,2,3,0,0),
(403,66,'2025-06-03','2025-06-03','CARREGAMENTO EMPRESA ABC LDA',167.86,'credit',0.00,8,NULL,1,0),
(404,66,'2025-06-08','2025-06-08','RESTAURANTE O PATEO',11.40,'debit',0.00,2,8,0,0),
(405,66,'2025-06-16','2025-06-16','PINGO DOCE ALFAMA',23.85,'debit',0.00,2,4,0,0),
(406,66,'2025-06-24','2025-06-24','PADARIA CENTRAL',6.70,'debit',0.00,NULL,NULL,0,0),
(407,66,'2025-06-28','2025-06-28','CONTINENTE BOM DIA',119.91,'debit',0.00,2,3,0,0),
(408,67,'2025-07-03','2025-07-03','CARREGAMENTO EMPRESA ABC LDA',167.86,'credit',0.00,8,NULL,1,0),
(409,67,'2025-07-07','2025-07-07','RESTAURANTE O PATEO',12.10,'debit',0.00,2,8,0,0),
(410,67,'2025-07-15','2025-07-15','PINGO DOCE ALFAMA',31.20,'debit',0.00,2,4,0,0),
(411,67,'2025-07-22','2025-07-22','PADARIA CENTRAL',5.90,'debit',0.00,NULL,NULL,0,0),
(412,67,'2025-07-28','2025-07-28','CONTINENTE BOM DIA',118.66,'debit',0.00,2,3,0,0),
(413,68,'2025-08-03','2025-08-03','CARREGAMENTO EMPRESA ABC LDA',167.86,'credit',0.00,8,NULL,1,0),
(414,68,'2025-08-08','2025-08-08','RESTAURANTE O PATEO',11.40,'debit',0.00,2,8,0,0),
(415,68,'2025-08-16','2025-08-16','PINGO DOCE ALFAMA',23.85,'debit',0.00,2,4,0,0),
(416,68,'2025-08-24','2025-08-24','PADARIA CENTRAL',6.70,'debit',0.00,NULL,NULL,0,0),
(417,68,'2025-08-28','2025-08-28','CONTINENTE BOM DIA',122.91,'debit',0.00,2,3,0,0),
(418,69,'2025-09-03','2025-09-03','CARREGAMENTO EMPRESA ABC LDA',167.86,'credit',0.00,8,NULL,1,0),
(419,69,'2025-09-07','2025-09-07','RESTAURANTE O PATEO',12.10,'debit',0.00,2,8,0,0),
(420,69,'2025-09-15','2025-09-15','PINGO DOCE ALFAMA',31.20,'debit',0.00,2,4,0,0),
(421,69,'2025-09-22','2025-09-22','PADARIA CENTRAL',5.90,'debit',0.00,NULL,NULL,0,0),
(422,69,'2025-09-28','2025-09-28','CONTINENTE BOM DIA',112.66,'debit',0.00,2,3,0,0),
(423,70,'2025-10-03','2025-10-03','CARREGAMENTO EMPRESA ABC LDA',167.86,'credit',0.00,8,NULL,1,0),
(424,70,'2025-10-08','2025-10-08','RESTAURANTE O PATEO',11.40,'debit',0.00,2,8,0,0),
(425,70,'2025-10-16','2025-10-16','PINGO DOCE ALFAMA',23.85,'debit',0.00,2,4,0,0),
(426,70,'2025-10-24','2025-10-24','PADARIA CENTRAL',6.70,'debit',0.00,NULL,NULL,0,0),
(427,70,'2025-10-28','2025-10-28','CONTINENTE BOM DIA',125.91,'debit',0.00,2,3,0,0),
(428,71,'2025-11-03','2025-11-03','CARREGAMENTO EMPRESA ABC LDA',167.86,'credit',0.00,8,NULL,1,0),
(429,71,'2025-11-07','2025-11-07','RESTAURANTE O PATEO',12.10,'debit',0.00,2,8,0,0),
(430,71,'2025-11-15','2025-11-15','PINGO DOCE ALFAMA',31.20,'debit',0.00,2,4,0,0),
(431,71,'2025-11-22','2025-11-22','PADARIA CENTRAL',5.90,'debit',0.00,NULL,NULL,0,0),
(432,71,'2025-11-28','2025-11-28','CONTINENTE BOM DIA',115.66,'debit',0.00,2,3,0,0),
(433,72,'2025-12-03','2025-12-03','CARREGAMENTO EMPRESA ABC LDA',167.86,'credit',0.00,8,NULL,1,0),
(434,72,'2025-12-08','2025-12-08','RESTAURANTE O PATEO',11.40,'debit',0.00,2,8,0,0),
(435,72,'2025-12-16','2025-12-16','PINGO DOCE ALFAMA',23.85,'debit',0.00,2,4,0,0),
(436,72,'2025-12-24','2025-12-24','PADARIA CENTRAL',6.70,'debit',0.00,NULL,NULL,0,0),
(437,72,'2025-12-28','2025-12-28','CONTINENTE BOM DIA',119.91,'debit',0.00,2,3,0,0),
(438,73,'2026-01-03','2026-01-03','CARREGAMENTO EMPRESA ABC LDA',167.86,'credit',0.00,8,NULL,1,0),
(439,73,'2026-01-07','2026-01-07','RESTAURANTE O PATEO',12.10,'debit',0.00,2,8,0,0),
(440,73,'2026-01-15','2026-01-15','PINGO DOCE ALFAMA',31.20,'debit',0.00,2,4,0,0),
(441,73,'2026-01-22','2026-01-22','PADARIA CENTRAL',5.90,'debit',0.00,NULL,NULL,0,0),
(442,73,'2026-01-28','2026-01-28','CONTINENTE BOM DIA',118.66,'debit',0.00,2,3,0,0),
(443,74,'2026-02-03','2026-02-03','CARREGAMENTO EMPRESA ABC LDA',167.86,'credit',0.00,8,NULL,1,0),
(444,74,'2026-02-08','2026-02-08','RESTAURANTE O PATEO',11.40,'debit',0.00,2,8,0,0),
(445,74,'2026-02-16','2026-02-16','PINGO DOCE ALFAMA',23.85,'debit',0.00,2,4,0,0),
(446,74,'2026-02-24','2026-02-24','PADARIA CENTRAL',6.70,'debit',0.00,NULL,NULL,0,0),
(447,74,'2026-02-28','2026-02-28','CONTINENTE BOM DIA',122.91,'debit',0.00,2,3,0,0),
(448,75,'2026-03-03','2026-03-03','CARREGAMENTO EMPRESA ABC LDA',167.86,'credit',0.00,8,NULL,1,0),
(449,75,'2026-03-07','2026-03-07','RESTAURANTE O PATEO',12.10,'debit',0.00,2,8,0,0),
(450,75,'2026-03-15','2026-03-15','PINGO DOCE ALFAMA',31.20,'debit',0.00,2,4,0,0),
(451,75,'2026-03-22','2026-03-22','PADARIA CENTRAL',5.90,'debit',0.00,NULL,NULL,0,0),
(452,75,'2026-03-28','2026-03-28','CONTINENTE BOM DIA',112.66,'debit',0.00,2,3,0,0);


-- ============================================================
-- 28. INVESTMENT ASSETS
--     1: the ETF the Trade Republic savings plan buys, as the import creates
--        it (matched by ISIN, no ticker, so no price fetch spends quota)
--     2: physical gold in grams, entered by hand
-- ============================================================

INSERT INTO [InvestmentAssets] ([Id],[AssetType],[Ticker],[Isin],[Name],[Notes],[ImportedAt]) VALUES
(1,'ETF',NULL,'IE00BK5BQT80','Vanguard Funds PLC - Vanguard FTSE All- World UCITS ETF (USD) Accumulating','Auto-created from a Trade Republic savings plan (IE00BK5BQT80). Set the ETF ticker to enable price updates.','2025-05-02 10:00:00'),
(2,'Gold',NULL,NULL,'Gold coins and bars','Demo note: physical gold, priced per gram','2025-04-20 10:00:00');


-- ============================================================
-- 29. INVESTMENT LOTS
--     1–12: one per savings-plan row in section 27: same date and quantity,
--           unit price = amount / quantity (4 dp), no fees
--     13–16: gold: three buys and one sell (negative quantity)
-- ============================================================

INSERT INTO [InvestmentLots] ([Id],[AssetId],[Date],[Quantity],[PricePerUnit],[Fees],[Notes],[ImportedAt]) VALUES
( 1,1,'2025-04-02',1.266892,118.4000,0,'Trade Republic savings plan','2025-04-02 10:00:00'),
( 2,1,'2025-05-02',1.248439,120.1500,0,'Trade Republic savings plan','2025-05-02 10:00:00'),
( 3,1,'2025-06-02',1.230517,121.9000,0,'Trade Republic savings plan','2025-06-02 10:00:00'),
( 4,1,'2025-07-02',1.206273,124.3500,0,'Trade Republic savings plan','2025-07-02 10:00:00'),
( 5,1,'2025-08-02',1.218522,123.1000,0,'Trade Republic savings plan','2025-08-02 10:00:00'),
( 6,1,'2025-09-02',1.182965,126.8000,0,'Trade Republic savings plan','2025-09-02 10:00:00'),
( 7,1,'2025-10-02',1.158749,129.4499,0,'Trade Republic savings plan','2025-10-02 10:00:00'),
( 8,1,'2025-11-02',1.170047,128.2000,0,'Trade Republic savings plan','2025-11-02 10:00:00'),
( 9,1,'2025-12-02',1.139818,131.6000,0,'Trade Republic savings plan','2025-12-02 10:00:00'),
(10,1,'2026-01-02',1.127396,133.0500,0,'Trade Republic savings plan','2026-01-02 10:00:00'),
(11,1,'2026-02-02',1.147666,130.7000,0,'Trade Republic savings plan','2026-02-02 10:00:00'),
(12,1,'2026-03-02',1.117318,134.2501,0,'Trade Republic savings plan','2026-03-02 10:00:00'),
(13,2,'2025-04-17',20,93.50,12.00,'Demo note: 20 g bar','2025-04-17 10:00:00'),
(14,2,'2025-08-21',10,99.40,8.00,'Demo note: two 5 g coins','2025-08-21 10:00:00'),
(15,2,'2025-12-11',15,106.20,9.50,NULL,'2025-12-11 10:00:00'),
(16,2,'2026-02-12',-12,109.60,6.00,'Demo note: sold part of the first bar','2026-02-12 10:00:00');


-- ============================================================
-- 30. INVESTMENT PRICE SNAPSHOTS  (both assets: the first of each month,
--     and every day of the last week of February 2026)
--     The last one falls on the first day of the newest month, so after
--     seed-demo.ps1 moves the dates it is never in the future.
-- ============================================================

INSERT INTO [InvestmentPriceSnapshots] ([Id],[AssetId],[Date],[PricePerUnit],[ImportedAt]) VALUES
( 1,1,'2025-04-01',118.40,'2025-04-01 18:00:00'),
( 2,1,'2025-05-01',120.15,'2025-05-01 18:00:00'),
( 3,1,'2025-06-01',121.90,'2025-06-01 18:00:00'),
( 4,1,'2025-07-01',124.35,'2025-07-01 18:00:00'),
( 5,1,'2025-08-01',123.10,'2025-08-01 18:00:00'),
( 6,1,'2025-09-01',126.80,'2025-09-01 18:00:00'),
( 7,1,'2025-10-01',129.45,'2025-10-01 18:00:00'),
( 8,1,'2025-11-01',128.20,'2025-11-01 18:00:00'),
( 9,1,'2025-12-01',131.60,'2025-12-01 18:00:00'),
(10,1,'2026-01-01',133.05,'2026-01-01 18:00:00'),
(11,1,'2026-02-01',130.70,'2026-02-01 18:00:00'),
(12,1,'2026-02-22',130.95,'2026-02-22 18:00:00'),
(13,1,'2026-02-23',131.40,'2026-02-23 18:00:00'),
(14,1,'2026-02-24',130.85,'2026-02-24 18:00:00'),
(15,1,'2026-02-25',132.10,'2026-02-25 18:00:00'),
(16,1,'2026-02-26',132.65,'2026-02-26 18:00:00'),
(17,1,'2026-02-27',133.20,'2026-02-27 18:00:00'),
(18,1,'2026-02-28',133.70,'2026-02-28 18:00:00'),
(19,1,'2026-03-01',134.25,'2026-03-01 18:00:00'),
(20,2,'2025-04-01',93.10,'2025-04-01 18:00:00'),
(21,2,'2025-05-01',94.80,'2025-05-01 18:00:00'),
(22,2,'2025-06-01',96.25,'2025-06-01 18:00:00'),
(23,2,'2025-07-01',97.40,'2025-07-01 18:00:00'),
(24,2,'2025-08-01',99.15,'2025-08-01 18:00:00'),
(25,2,'2025-09-01',101.60,'2025-09-01 18:00:00'),
(26,2,'2025-10-01',104.30,'2025-10-01 18:00:00'),
(27,2,'2025-11-01',103.75,'2025-11-01 18:00:00'),
(28,2,'2025-12-01',106.90,'2025-12-01 18:00:00'),
(29,2,'2026-01-01',109.20,'2026-01-01 18:00:00'),
(30,2,'2026-02-01',108.45,'2026-02-01 18:00:00'),
(31,2,'2026-02-22',108.70,'2026-02-22 18:00:00'),
(32,2,'2026-02-23',109.15,'2026-02-23 18:00:00'),
(33,2,'2026-02-24',109.80,'2026-02-24 18:00:00'),
(34,2,'2026-02-25',110.25,'2026-02-25 18:00:00'),
(35,2,'2026-02-26',110.90,'2026-02-26 18:00:00'),
(36,2,'2026-02-27',111.35,'2026-02-27 18:00:00'),
(37,2,'2026-02-28',111.60,'2026-02-28 18:00:00'),
(38,2,'2026-03-01',111.80,'2026-03-01 18:00:00');


-- ============================================================
-- 31. MICRO1 CONTRACT (profile 4, categories 14–16, slips 31–33)
--     As a micro1 invoice paired with its Deel withdrawal is saved: EUR
--     amounts, net = EUR received (the Deel credits in section 27), the
--     'Deel exchange fee' as a deduction, income − deductions = net.
--     Slip 33 (Mar 2026) is a second pay run merged into the month: hours
--     and amounts summed, both invoices named in SourceFile, the hourly
--     rate weighted by hours.
--     The profile's name starts with an accented letter (DISPLAY_ORDER).
-- ============================================================

INSERT INTO [SalaryProfiles] ([Id],[Name],[Description],[HourlyRateFormula]) VALUES
(4,'Échelle Labs (micro1)','Hourly contract paid in USD through Deel','hours');

INSERT INTO [SalaryItemCategories] ([Id],[SalaryProfileId],[Name],[Color],[ItemType],[IsProtected]) VALUES
(14,4,'Base Pay',         '#3b82f6','income',   0),
(15,4,'Other',            '#93c5fd','income',   0),
(16,4,'Deel exchange fee','#f87171','deduction',0);

INSERT INTO [SalarySlips]
  ([Id],[SalaryProfileId],[Period],[GrossAmount],[NetAmount],[Notes],[SourceFile],[PdfPath],[FileHash],[ImportedAt],[BaseAmount],[HoursWorked],[HourlyRate],[TotalEspecie])
VALUES
(31,4,'2026-01-01',2135.78,2114.42,NULL,'micro1_invoice_2026_01.pdf',NULL,NULL,'2026-02-03 10:00:00',2135.78,80.00,26.70,NULL),
(32,4,'2026-02-01',2169.22,2147.53,'Demo note: includes a one-off USD 50 bonus','micro1_invoice_2026_02.pdf',NULL,NULL,'2026-03-03 10:00:00',2126.35,80.00,26.58,NULL),
(33,4,'2026-03-01',2185.27,2163.42,'Demo note: two pay runs merged into one month','micro1_invoice_2026_03a.pdf; micro1_invoice_2026_03b.pdf',NULL,NULL,'2026-04-03 10:00:00',2185.27,82.00,26.65,NULL);

INSERT INTO [SalaryLineItems]
  ([Id],[SalarySlipId],[SalaryItemCategoryId],[Amount],[SortOrder],[Quantity],[UnitValue],[Percentage],[IncidenciaBase])
VALUES
(112,31,14,2135.78,0,NULL,NULL,NULL,NULL),
(113,31,16,21.36,1,NULL,NULL,NULL,NULL),
(114,32,14,2126.35,0,NULL,NULL,NULL,NULL),
(115,32,15,42.87,1,NULL,NULL,NULL,NULL),
(116,32,16,21.69,2,NULL,NULL,NULL,NULL),
(117,33,14,2185.27,0,NULL,NULL,NULL,NULL),
(118,33,16,21.85,1,NULL,NULL,NULL,NULL);


-- ============================================================
-- 32. GROCERY RECEIPT CATEGORIES
--     Continente receipts print a section name over each item
--     (GroceryItems.ReceiptCategory); a mapping files every item of that
--     section under a grocery category when no rule matches it.
--     Grocery category 9 starts with an accented letter (DISPLAY_ORDER).
--     'Congelados' is left unmapped, as a new section looks after upload.
-- ============================================================

INSERT INTO [GroceryCategories] ([Id],[Name],[Color],[IsProtected]) VALUES
(9,'Óleos & Condimentos','#facc15',0);

INSERT INTO [GroceryReceiptCategoryMappings] ([Id],[ReceiptCategoryName],[GroceryCategoryId]) VALUES
(1,'Frutas e Legumes',1),
(2,'Laticínios',2),
(3,'Talho e Peixaria',3),
(4,'Padaria',4),
(5,'Bebidas',5),
(6,'Limpeza',6),
(7,'Higiene',7),
(8,'Azeites e Óleos',9);

-- The Continente receipts above carry their section names.
UPDATE [GroceryItems]
SET ReceiptCategory = CASE CategoryId
    WHEN 1 THEN 'Frutas e Legumes'
    WHEN 2 THEN 'Laticínios'
    WHEN 3 THEN 'Talho e Peixaria'
    WHEN 4 THEN 'Padaria'
    WHEN 5 THEN 'Bebidas'
    WHEN 6 THEN 'Limpeza'
    WHEN 7 THEN 'Higiene'
END
WHERE ReceiptId BETWEEN 1 AND 12;

-- Receipt 28: items filed by their section's mapping (no rule, not manual),
-- one by a rule, and one in the unmapped section, so uncategorised.
INSERT INTO [GroceryReceipts] ([Id],[StoreName],[ReceiptDate],[Total],[Notes],[SourceFile],[PdfPath],[FileHash],[ImportedAt]) VALUES
(28,'Continente','2026-03-21',17.55,NULL,NULL,NULL,NULL,'2026-03-21T00:00:00');

INSERT INTO [GroceryItems] ([Id],[ReceiptId],[Description],[Amount],[Quantity],[ReceiptCategory],[CategoryId],[CategoryRuleId],[CategorySetManually],[IsExcluded]) VALUES
(124,28,'LEITE MIMOSA',1.89,1,'Laticínios',2,4,0,0),
(125,28,'AZEITE VIRGEM EXTRA',7.49,1,'Azeites e Óleos',9,NULL,0,0),
(126,28,'MACAS GALA',2.39,1,'Frutas e Legumes',1,NULL,0,0),
(127,28,'ESPINAFRES',1.79,1,'Frutas e Legumes',1,NULL,0,0),
(128,28,'GELADO BAUNILHA',3.99,1,'Congelados',NULL,NULL,0,0);


-- ============================================================
COMMIT TRANSACTION;
-- ================================================
-- Demo seed complete.
--   11 categories, 34 rules
--   75 statements (ActivoBank/BPI/Revolut/CGD/Trade Republic/MEAL CARD 12 each; MBcp 3)
--   452 transactions (excluded: 5 above, the internal transfers and the
--     12 savings-plan buys; 2 amounts >= 10 000)
--   4 salary profiles, 33 slips, 118 line items
--     Profiles 1–3 have matching bank income (CGD account); profile 4's
--     Deel payouts land on Trade Republic
--     Profile 3 gross up to 8 500; slip 30 triggers salary parse warning
--     Slip 33 merges two micro1 pay runs
--   9 grocery categories, 15 rules, 8 receipt category mappings
--   28 receipts (Continente 13 + Pingo Doce 12 + Mercadona 3)
--   128 items (3 excluded; 1 item @ 150.00; 1 item qty 12)
--   2 investment assets, 16 lots (1 sell), 38 price snapshots
--   MBcp statement 39 triggers balance mismatch parse warning
-- ================================================
