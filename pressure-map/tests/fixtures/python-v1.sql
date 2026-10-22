BEGIN TRANSACTION;
CREATE TABLE courses (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  semester_id INTEGER NOT NULL REFERENCES semesters(id) ON DELETE CASCADE,
  name        TEXT NOT NULL,
  raw_text    TEXT NOT NULL DEFAULT '',
  position    INTEGER NOT NULL DEFAULT 0
);
INSERT INTO "courses" VALUES(1,1,'高等数学','成绩构成：平时 30%，期中 20%，期末 50%。
第8周进行期中考试，范围为第1到第5章。
第13周随堂测验（第十章），计入平时成绩。
期末考试安排在第17周，闭卷。',0);
INSERT INTO "courses" VALUES(2,1,'数据结构','实验一至实验三的实验报告请于第6周周五前提交。
课程设计于第10周前提交源码与设计说明。
第16周进行上机考核，内容为综合编程题。',1);
INSERT INTO "courses" VALUES(3,1,'大学英语','第9周进行小组口语展示，每组5分钟。
第11周期中测验，含听力。
期末考试在第17周，笔试加听力。',2);
INSERT INTO "courses" VALUES(4,1,'思想政治','第7周提交一篇1500字的小论文。
第14周提交读书报告。
期末考查时间安排在学期末，具体周次另行通知。',3);
CREATE TABLE desktop_todos (
  id TEXT PRIMARY KEY,
  title TEXT NOT NULL,
  due_date TEXT,
  important INTEGER NOT NULL DEFAULT 0 CHECK (important IN (0,1)),
  completed INTEGER NOT NULL DEFAULT 0 CHECK (completed IN (0,1)),
  source_key TEXT UNIQUE,
  source_label TEXT NOT NULL DEFAULT '',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
INSERT INTO "desktop_todos" VALUES('4dedb37cec0840b897232811b953f5da','期中考试','2026-11-01',1,1,'5651ebcfd482090836e9773f6bb7a42f4eaaafcacbc957ab6e90bb945a44e8fa','Python 旧版数据 / 高等数学 / 第 8 周','2026-10-19T14:03:58','2026-10-19T14:03:58');
INSERT INTO "desktop_todos" VALUES('fb193dbf08154d68807757efb2fedae8','随堂测验（第十章）','2026-12-06',0,0,'30217aec25641c7233d5c913a2577db415009a78af3982a7b1929a5f59fe1541','Python 旧版数据 / 高等数学 / 第 13 周','2026-10-19T14:03:58','2026-10-19T14:03:58');
INSERT INTO "desktop_todos" VALUES('8565f2e2e83047f790ec82a5180df28b','期末考试','2027-01-03',1,0,'b7684a72647100fad21782e72f3c952a841ff73f9e52ebe90c33c74608977814','Python 旧版数据 / 高等数学 / 第 17 周','2026-10-19T14:03:58','2026-10-19T14:03:58');
INSERT INTO "desktop_todos" VALUES('fc8603ac8fc3446f87bddd51506b9c90','实验报告（实验一至三）','2026-10-18',0,0,'5ba273a23bd85e8cd162b4466dae267a5992119886134e345ecac861ecb08e1b','Python 旧版数据 / 数据结构 / 第 6 周','2026-10-19T14:03:58','2026-10-19T14:03:58');
INSERT INTO "desktop_todos" VALUES('aa2759182fad4b98a8aa4144620a3438','课程设计','2026-11-15',1,0,'b8de84d1837b2b47adccf540bc49beb133e9b5a73ab4796053380411fb996670','Python 旧版数据 / 数据结构 / 第 10 周','2026-10-19T14:03:58','2026-10-19T14:03:58');
INSERT INTO "desktop_todos" VALUES('c8fc3962728d46a78a03d49a9b87f7a4','上机考核','2026-12-27',1,0,'72e472923484bebbb68312506a5246087f8eff3b0a92c02af7034162ea2f3b05','Python 旧版数据 / 数据结构 / 第 16 周','2026-10-19T14:03:58','2026-10-19T14:03:58');
INSERT INTO "desktop_todos" VALUES('1605d9ee9e5b4a9eb8ac2460455bbfef','小组口语展示','2026-11-08',0,0,'3369e515710802c24b8b06fe4958a52b922022724b83ec8ef27dfa216578389f','Python 旧版数据 / 大学英语 / 第 9 周','2026-10-19T14:03:58','2026-10-19T14:03:58');
INSERT INTO "desktop_todos" VALUES('b2d2843919304aea98bf3b43f6c527b4','期中测验','2026-11-22',0,0,'fba4656dd0209d675fe706c34f83484529a03e90352f1bdb7c79063ba7822d0c','Python 旧版数据 / 大学英语 / 第 11 周','2026-10-19T14:03:58','2026-10-19T14:03:58');
INSERT INTO "desktop_todos" VALUES('bb91872faa9342cab5f2b721bf4158f3','期末考试','2027-01-03',1,0,'def739a6ec43d81e840c3233d626e0e0791f88b9dc2cba12bd499abbefe9b67e','Python 旧版数据 / 大学英语 / 第 17 周','2026-10-19T14:03:58','2026-10-19T14:03:58');
INSERT INTO "desktop_todos" VALUES('02904e0fe4d745d38443b48845c2288d','小论文','2026-10-25',0,0,'6e21771192b5bfcc357146fdb70ae30ca4dabb08650aec6a0684a80f76dba205','Python 旧版数据 / 思想政治 / 第 7 周','2026-10-19T14:03:58','2026-10-19T14:03:58');
INSERT INTO "desktop_todos" VALUES('b0f524b22ca24369963cd0aa18999845','读书报告','2026-12-13',0,0,'e584e1b9a4f2f94ae2b1083b4e95e3c6df793928701da08567cf65b649292829','Python 旧版数据 / 思想政治 / 第 14 周','2026-10-19T14:03:58','2026-10-19T14:03:58');
INSERT INTO "desktop_todos" VALUES('28a495592cf44fbba0b1354e051b48e0','期末考查',NULL,1,0,'2fa20ee7c518e6735533dc78df72cf92b2e8adbac5256ca896588c29b67fcf4f','Python 旧版数据 / 思想政治 / 截止周待确认','2026-10-19T14:03:58','2026-10-19T14:03:58');
CREATE TABLE semesters (
  id             INTEGER PRIMARY KEY AUTOINCREMENT,
  name           TEXT NOT NULL,
  total_weeks    INTEGER NOT NULL DEFAULT 18,
  week1_monday   TEXT,
  threshold      REAL NOT NULL DEFAULT 7,
  hours_per_week REAL NOT NULL DEFAULT 20,
  type_hours     TEXT NOT NULL DEFAULT '{}',
  current_step   INTEGER NOT NULL DEFAULT 1,
  created_at     TEXT NOT NULL,
  updated_at     TEXT NOT NULL
);
INSERT INTO "semesters" VALUES(1,'Python 旧版数据',18,'2026-09-07',7.0,20.0,'{"考试": 15, "大作业": 20, "报告": 8, "测验": 4, "其他": 4}',1,'2026-10-19T14:03:58','2026-10-19T14:03:58');
CREATE TABLE tasks (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  semester_id INTEGER NOT NULL REFERENCES semesters(id) ON DELETE CASCADE,
  course_id   INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
  name        TEXT NOT NULL,
  type        TEXT NOT NULL CHECK (type IN ('考试','大作业','报告','测验','其他')),
  week        INTEGER,
  weight      REAL NOT NULL,
  est_hours   REAL,
  source      TEXT NOT NULL DEFAULT '',
  position    INTEGER NOT NULL DEFAULT 0
);
INSERT INTO "tasks" VALUES(1,1,1,'期中考试','考试',8,5.0,NULL,'第8周进行期中考试，范围为第1到第5章。',0);
INSERT INTO "tasks" VALUES(2,1,1,'随堂测验（第十章）','测验',13,2.0,NULL,'第13周随堂测验（第十章），计入平时成绩。',1);
INSERT INTO "tasks" VALUES(3,1,1,'期末考试','考试',17,5.0,NULL,'期末考试安排在第17周，闭卷。',2);
INSERT INTO "tasks" VALUES(4,1,2,'实验报告（实验一至三）','报告',6,3.0,NULL,'实验一至实验三的实验报告请于第6周周五前提交。',3);
INSERT INTO "tasks" VALUES(5,1,2,'课程设计','大作业',10,4.0,NULL,'课程设计于第10周前提交源码与设计说明。',4);
INSERT INTO "tasks" VALUES(6,1,2,'上机考核','考试',16,5.0,NULL,'第16周进行上机考核，内容为综合编程题。',5);
INSERT INTO "tasks" VALUES(7,1,3,'小组口语展示','其他',9,2.0,NULL,'第9周进行小组口语展示，每组5分钟。',6);
INSERT INTO "tasks" VALUES(8,1,3,'期中测验','测验',11,2.0,NULL,'第11周期中测验，含听力。',7);
INSERT INTO "tasks" VALUES(9,1,3,'期末考试','考试',17,5.0,NULL,'期末考试在第17周，笔试加听力。',8);
INSERT INTO "tasks" VALUES(10,1,4,'小论文','报告',7,3.0,NULL,'第7周提交一篇1500字的小论文。',9);
INSERT INTO "tasks" VALUES(11,1,4,'读书报告','报告',14,3.0,NULL,'第14周提交读书报告。',10);
INSERT INTO "tasks" VALUES(12,1,4,'期末考查','考试',NULL,5.0,NULL,'期末考查时间安排在学期末，具体周次另行通知。',11);
CREATE INDEX idx_courses_semester ON courses(semester_id);
CREATE INDEX idx_tasks_semester ON tasks(semester_id);
DELETE FROM "sqlite_sequence";
INSERT INTO "sqlite_sequence" VALUES('semesters',1);
INSERT INTO "sqlite_sequence" VALUES('courses',4);
INSERT INTO "sqlite_sequence" VALUES('tasks',12);
COMMIT;