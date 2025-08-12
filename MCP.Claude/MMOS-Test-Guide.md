# MMOS Test Rehberi
## Multi-Model Orchestration System Test Workflow

### 📋 Sistem Gereksinimleri
- MSSQL Server (DEV-JH\DEVOGUZ)
- MCP_DB database (tablolar oluşturulmuş)
- 3 ayrı terminal/PowerShell penceresi
- Claude Code CLI kurulumu

### 🚀 Test Workflow Adımları

#### 1️⃣ AdminClaude Instance Başlatma
```bash
cd "D:\Source\Oguz\MCPs\MCP.Claude\AdminClaude"
claude-code
```

**Beklenen Durum**: 
- OrchestratorMcp server aktif
- Enhanced SQL server aktif
- DateTime server aktif

**İlk Test Komutları**:
```bash
# Sistem sağlığını kontrol et
/mcp-tools # Kullanılabilir tool'ları listele
```

#### 2️⃣ WorkerClaude1 Instance Başlatma
```bash
cd "D:\Source\Oguz\MCPs\MCP.Claude\WorkerClaude1"
claude-code
```

**Worker Registration**:
```bash
# Worker'ı sisteme kaydet
RegisterWorker
# Durum kontrolü
GetWorkerStatus
```

#### 3️⃣ WorkerClaude2 Instance Başlatma
```bash
cd "D:\Source\Oguz\MCPs\MCP.Claude\WorkerClaude2"
claude-code
```

**Worker Registration**:
```bash
# Worker'ı sisteme kaydet
RegisterWorker
# Durum kontrolü  
GetWorkerStatus
```

#### 4️⃣ AdminClaude'da Worker Discovery
AdminClaude terminal'ine geri dön ve worker'ları keşfet:

```bash
# Tüm worker'ları keşfet
DiscoverWorkers

# Sistem sağlık kontrolü
SystemHealthCheck
```

**Beklenen Çıktı**:
```
🔍 Worker Discovery Raporu

📊 Özet:
   • Toplam Worker: 2
   • Aktif Worker: 2
   • Uygun Worker: 2

👥 Worker Listesi:
🟢 Worker-Claude-1 (Claude)
   └─ Status: IDLE, Tasks: 0, Load: 0
   └─ Inactive: 0 dakika

🟢 Worker-Claude-2 (Claude)
   └─ Status: IDLE, Tasks: 0, Load: 0
   └─ Inactive: 0 dakika
```

#### 5️⃣ İlk Proje ve Task Oluşturma
AdminClaude'da:

```bash
# Yeni proje oluştur
CreateProject "Test API Development" "Simple REST API with authentication" 8

# Task decomposition yap
DecomposeTask 1 "Create a REST API with user authentication and basic CRUD operations" "[]" true

# Proje durumunu kontrol et
GetProjectStatus 1
```

#### 6️⃣ Task Assignment Test
AdminClaude'da:

```bash
# Otomatik task assignment
AssignTaskToWorker 2 "auto" 45

# Manuel task assignment
AssignTaskToWorker 3 "Worker-Claude-2" 30

# Proje durumunu tekrar kontrol et
GetProjectStatus 1
```

#### 7️⃣ Worker Task Execution Simulation
**WorkerClaude1'de**:
```bash
# Atanmış task'ları kontrol et
PollForTasks

# Task'ı kabul et
AcceptTask 2

# Progress raporu
ReportProgress 2 25 "API schema design completed"
ReportProgress 2 50 "Authentication endpoints implemented"
ReportProgress 2 75 "CRUD operations added"

# Task'ı tamamla
CompleteTask 2 "REST API with authentication successfully implemented. Includes user registration, login, and basic CRUD operations for user management." true
```

**WorkerClaude2'de**:
```bash
# Atanmış task'ları kontrol et
PollForTasks

# Task'ı kabul et  
AcceptTask 3

# Progress raporu
ReportProgress 3 30 "Test framework setup completed"
ReportProgress 3 70 "Unit tests for authentication written"

# Task'ı tamamla
CompleteTask 3 "Comprehensive test suite created covering authentication flows and CRUD operations. All tests passing." true
```

#### 8️⃣ Final Status Check
AdminClaude'da final durum kontrolü:

```bash
# Proje durumu
GetProjectStatus 1

# Worker durumu
DiscoverWorkers

# Genel sistem sağlığı
SystemHealthCheck
```

**Beklenen Final Durumu**:
- 2 task completed
- 2 worker idle durumda
- Proje progress %X tamamlanmış

### 🧪 İleri Düzey Test Senaryoları

#### Test 1: Parallel Task Execution
```bash
# AdminClaude'da
CreateProject "Parallel Development" "Multi-component system development" 7
DecomposeTask 2 "Build a web application with frontend and backend components" "[]" true

# Birden fazla task'ı eş zamanlı ata
AssignTaskToWorker 4 "Worker-Claude-1" 60
AssignTaskToWorker 5 "Worker-Claude-2" 60

# Her iki worker'da eş zamanlı task execution simülasyonu
```

#### Test 2: Worker Failure Simulation
```bash
# WorkerClaude1'de task kabul et ama tamamlama
AcceptTask X
# Task'ı failed olarak işaretle
CompleteTask X "Encountered critical error during implementation" false

# AdminClaude'da failure handling kontrolü
SystemHealthCheck
```

#### Test 3: Load Balancing Test
```bash
# AdminClaude'da çoklu task oluştur ve otomatik assignment
CreateProject "Load Test" "Multiple small tasks for load testing" 5
# Multiple DecomposeTask calls
# Multiple AssignTaskToWorker "auto" calls
```

### 📊 Test Başarı Kriterleri

✅ **Temel Fonksiyonalite**:
- [ ] 3 instance başarıyla başlatılır
- [ ] Worker registration çalışır
- [ ] Worker discovery çalışır  
- [ ] Task creation ve assignment çalışır
- [ ] Task execution workflow tamamlanır

✅ **Sistem Koordinasyonu**:
- [ ] Database synchronization doğru çalışır
- [ ] Worker status tracking doğru
- [ ] Task status transitions doğru
- [ ] Progress reporting çalışır

✅ **Error Handling**:
- [ ] Offline worker detection
- [ ] Task failure handling
- [ ] Connection error recovery

### 🔧 Troubleshooting

#### Problem: Worker kayıt olmuyor
**Çözüm**: 
- Database connection string kontrolü
- MCP server başlangıç logları kontrolü
- Environment variables kontrolü

#### Problem: Task assignment çalışmıyor  
**Çözüm**:
- Worker idle status kontrolü
- Task status database'de kontrolü
- Foreign key constraint hatalarını kontrol et

#### Problem: Cross-instance communication çalışmıyor
**Çözüm**:
- Database permissions kontrolü
- MCP server loglarını incele
- Connection timeout ayarları kontrol et

### 📝 Test Sonuçları Dokümantasyonu

Test sonuçlarını kaydetmek için her aşamada:

1. **Screenshot'lar**: Her major step'in çıktısı
2. **Timing**: Task completion süreleri
3. **Error Logs**: Herhangi bir hata durumu
4. **Performance**: System resource usage

### 🎯 Sonraki Adımlar

Test başarılı olduktan sonra:
- Production deployment konfigürasyonu
- Additional worker instance'lar ekleme
- Web dashboard development
- Performance optimization
- Advanced routing algorithms

---

**Not**: Bu test rehberi MMOS'un temel functionality'sini doğrulamak için tasarlanmıştır. Gerçek production kullanımında additional security, monitoring ve error handling özelliklerine ihtiyaç olacaktır.