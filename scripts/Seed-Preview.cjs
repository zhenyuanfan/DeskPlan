// Writes fictional UI fixtures only into an explicitly named temporary QA database.
const { DatabaseSync } = require('node:sqlite');
const path = require('node:path');
const directory = path.resolve(process.argv[2]);
if (!path.basename(directory).startsWith('DeskPlan-QA-')) throw new Error('Only a QA directory is allowed.');
const db = new DatabaseSync(path.join(directory, 'deskplan.db'));
if (db.prepare('SELECT count(*) AS n FROM tasks').get().n !== 0) throw new Error('QA database must be empty.');
const now = new Date();
const iso = d => `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
const day = delta => { const d = new Date(now); d.setDate(d.getDate()+delta); return iso(d); };
const insert = db.prepare(`INSERT INTO tasks(title,scheduled_date,due_date,start_minutes,end_minutes,status,sort_order,created_at,updated_at)
 VALUES(?,?,?,?,?,?,?,?,?)`);
const add = (title,start,end,time,status=0) => insert.run(title,start,end,time,time === null ? null : time+30,status,0,now.toISOString(),now.toISOString());
add('整理项目方案',day(-1),day(1),null);
add('设计评审',day(0),null,600);
add('完成本周工作记录',day(0),null,1035);
add('确认需求清单',day(0),null,null,2);
add('长标题示例：核对下周交付内容并整理反馈意见',day(0),null,null);
add('准备演示材料',day(2),day(4),null);
add('团队沟通',day(4),null,900);
db.close();
