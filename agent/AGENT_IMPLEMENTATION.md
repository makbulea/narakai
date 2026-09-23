# Narakai Agent Implementation

Mevcut NARAKAI projesine Agent katmanını
implement etmeni istiyorum.

ÖNEMLİ:

Projede halihazırda:

1. Backend microservice altyapısı
2. RAG servisi
3. Repository indexing
4. Chunking
5. Embedding
6. PostgreSQL + pgvector
7. Semantic/vector search

çalışmaları bulunmaktadır.

Öncelikle mevcut repository'yi analiz et.

Mevcut RAG ve backend kodunu yeniden yazma.
Mevcut servisleri mümkün olduğunca reuse et.

Amacımız RAG'in üzerine bir Agent orchestration katmanı
eklemektir.


==================================================
1. PROJENİN AMACI
==================================================

Narakai, geliştiricilerin karmaşık bir codebase hakkında
doğal dil ile soru sorabilmesini sağlayan agentic
code intelligence platformudur.

Örnek sorular:

- Order oluşturulduğunda hangi servisler devreye giriyor?
- PaymentService'i kimler çağırıyor?
- Bu event nerede publish ediliyor?
- Bu sınıf hangi interface'leri implement ediyor?
- Bu method'un caller'ları kim?
- Bu servis hangi Kafka topic'lerini kullanıyor?
- Kullanıcı login olduğunda hangi servisler çalışıyor?
- Bu business flow hangi dosyalardan geçiyor?
- Bu kodun sorumluluğu nedir?
- Bu değişiklik başka hangi servisleri etkileyebilir?

Agent yalnızca semantic search sonucu döndürmemelidir.

Gerekirse:

RAG
+
Code Search
+
File Reading
+
Reference Search
+
Dependency Analysis
+
Event Analysis

gibi araçları kullanarak araştırma yapmalıdır.


==================================================
2. AGENT'IN TEMEL SORUMLULUĞU
==================================================

Agent'ın görevi:

1. Kullanıcının sorusunu anlamak
2. Sorunun tipini belirlemek
3. Gerekli bilgiyi RAG üzerinden aramak
4. RAG sonuçlarının yeterli olup olmadığını değerlendirmek
5. Gerekirse tool calling yapmak
6. Tool sonuçlarını analiz etmek
7. Gerekirse tekrar arama yapmak
8. Elde edilen bilgileri birleştirmek
9. Kanıta dayalı bir cevap üretmek

Agent hiçbir zaman kod hakkında bilgi uydurmamalıdır.

Repository'de bulunmayan bir bilgi için bunu açıkça belirtmelidir.


==================================================
3. AGENT WORKFLOW
==================================================

Aşağıdaki workflow'u implement et:

User Query
    ↓
Query Understanding
    ↓
Initial RAG Retrieval
    ↓
Context Evaluation
    ↓
Tool Selection
    ↓
Tool Execution
    ↓
Result Analysis
    ↓
Additional Retrieval / Tool Call
    ↓
Final Answer


Agent gerektiğinde workflow'u tekrar edebilmelidir.


==================================================
4. AGENT TOOLS
==================================================

Tool'ları bağımsız abstraction olarak tasarla.

En az aşağıdaki tool'lar bulunmalıdır.


--------------------------------------------------
4.1 SearchCode
--------------------------------------------------

Amaç:

Repository içerisinde semantic veya text based
arama yapmak.

Input:

- query
- repositoryId
- optional filePath
- optional language

Output:

- filePath
- startLine
- endLine
- content
- relevanceScore


--------------------------------------------------
4.2 ReadFile
--------------------------------------------------

Amaç:

Belirli bir dosyanın içeriğini okumak.

Input:

- repositoryId
- filePath
- startLine
- endLine

Output:

- filePath
- content


--------------------------------------------------
4.3 FindReferences
--------------------------------------------------

Amaç:

Bir class, method, interface veya symbol'e yapılan
referansları bulmak.

Input:

- repositoryId
- symbol
- filePath

Output:

- referencing file
- line
- code
- relationship


--------------------------------------------------
4.4 FindCallers
--------------------------------------------------

Amaç:

Bir method'u kimlerin çağırdığını bulmak.

Input:

- repositoryId
- className
- methodName

Output:

- caller
- file
- line
- code


--------------------------------------------------
4.5 FindImplementations
--------------------------------------------------

Amaç:

Bir interface veya abstract class'ın implementasyonlarını
bulmak.

Input:

- repositoryId
- symbol

Output:

- implementation
- file
- line


--------------------------------------------------
4.6 FindEvents
--------------------------------------------------

Amaç:

Event publish ve consume noktalarını bulmak.

Örneğin:

OrderCreated

aranınca:

Publisher:
OrderService

Consumers:
PaymentService
InventoryService

gibi sonuç üretilebilmelidir.


==================================================
5. RAG INTEGRATION
==================================================

Agent mevcut RAG servisini tool olarak kullanmalıdır.

Örneğin:

SearchCode("Order oluşturulduğunda")

çağrıldığında RAG:

1. Query embedding üretmeli
2. pgvector üzerinde similarity search yapmalı
3. İlgili chunk'ları getirmeli
4. Varsa reranking uygulamalı
5. Sonuçları Agent'a dönmelidir.

Agent RAG sonuçlarını doğrudan final cevap olarak
kullanmak zorunda değildir.

RAG sonuçlarını başlangıç bilgisi olarak değerlendirmelidir.


==================================================
6. TOOL SELECTION
==================================================

Tool seçimini LLM yapmalıdır.

Örneğin:

Question:
"PaymentService'i kim çağırıyor?"

Muhtemel plan:

SearchCode
    ↓
PaymentService
    ↓
FindCallers
    ↓
Analyze
    ↓
Answer


Başka örnek:

Question:
"Order oluşturulduğunda hangi servisler çalışıyor?"

Plan:

SearchCode
    ↓
Order creation
    ↓
FindReferences
    ↓
FindEvents
    ↓
FindConsumers
    ↓
Analyze dependency chain
    ↓
Answer


==================================================
7. AGENT LOOP
==================================================

Agent tek bir tool çağrısından sonra zorunlu olarak
cevap vermemelidir.

Şuna benzer bir loop oluştur:

while (!answerReady)
{
    AnalyzeContext();

    if (NeedMoreInformation())
    {
        tool = SelectTool();

        result = ExecuteTool(tool);

        AddResultToContext(result);
    }
    else
    {
        answer = GenerateAnswer();
    }
}

Ancak sonsuz loop oluşmasını engelle.

Maximum iteration / tool call limiti ekle.

Örneğin:

MaxIterations = 8


==================================================
8. EVIDENCE-BASED ANSWERS
==================================================

Agent cevaplarında mümkün olduğunca kaynak göstermelidir.

Örneğin:

Order oluşturulduğunda ilk olarak:

1. OrderService.cs:42
2. OrderCreatedHandler.cs:18
3. PaymentConsumer.cs:31
4. InventoryConsumer.cs:27

devreye girmektedir.

Cevap:

"OrderService içerisinde OrderCreated event'i publish
ediliyor. Bu event PaymentConsumer ve InventoryConsumer
tarafından consume ediliyor."

Sources:

- OrderService.cs:42
- OrderCreatedHandler.cs:18
- PaymentConsumer.cs:31
- InventoryConsumer.cs:27


==================================================
9. AGENT MEMORY
==================================================

İlk versiyonda uzun süreli memory implement etme.

Conversation context destekle.

Agent aynı conversation içerisinde önceki mesajları
kullanabilmelidir.

Örneğin:

User:
"OrderService'i bul."

Agent:
"OrderService.cs bulundu."

User:
"Kim çağırıyor?"

Agent:
"OrderService için şu caller'lar bulundu..."

İkinci soruda önceki context kullanılmalıdır.


==================================================
10. ARCHITECTURE
==================================================

Agent kodunu mevcut backend architecture ile uyumlu
şekilde geliştir.

Önerilen yapı:

Agent
├── AgentService
├── AgentOrchestrator
├── PromptManager
├── ToolRegistry
├── ToolExecutor
├── ConversationContext
├── AgentState
└── Models

Tools
├── SearchCodeTool
├── ReadFileTool
├── FindReferencesTool
├── FindCallersTool
├── FindImplementationsTool
└── FindEventsTool


Tool'lar AgentService içerisine gömülmemelidir.

Yeni tool eklemek kolay olmalıdır.


==================================================
11. LLM ABSTRACTION
==================================================

LLM provider'ını Agent koduna hard-code etme.

Örneğin:

ILLMClient

veya mevcut projede uygun olan abstraction'ı kullan.

Agent:

Agent
 ↓
ILLMClient
 ↓
OpenAI / Azure OpenAI / Local LLM

şeklinde çalışabilmelidir.

API key'leri source code içine yazma.


==================================================
12. STRUCTURED TOOL CALLING
==================================================

LLM'in tool çağrılarını structured şekilde yapmasını sağla.

Örneğin:

{
    "tool": "find_callers",
    "arguments": {
        "className": "PaymentService",
        "methodName": "ProcessPayment"
    }
}

Tool sonuçlarını Agent context'ine ekle.

Tool çağrıları ve sonuçları loglanabilmelidir.


==================================================
13. LOGGING / OBSERVABILITY
==================================================

Agent'ın ne yaptığını takip edebilmek istiyorum.

Her Agent execution için:

- correlationId
- conversationId
- query
- selected tool
- tool arguments
- tool execution time
- tool result summary
- iteration number
- final answer

loglanmalıdır.

Sensitive data loglama.


==================================================
14. ERROR HANDLING
==================================================

Tool hata verirse Agent tamamen çökmemeli.

Örneğin:

FindCallers failed

durumunda Agent:

- hatayı context'e ekleyebilir
- alternatif tool deneyebilir
- veya kullanıcıya yeterli bilgi bulunamadığını
  söyleyebilir.

LLM API failure için retry mekanizması kullan.

Retry için exponential backoff uygula.

Fakat sonsuz retry yapma.


==================================================
15. SECURITY
==================================================

Aşağıdakilere dikkat et:

- API key source code içinde bulunmamalı
- User input doğrudan system prompt'a güvenilmemeli
- Tool arguments validate edilmeli
- Repository access authorization kontrol edilmeli
- Path traversal engellenmeli
- Agent'ın repository dışındaki dosyalara erişmesine izin verme
- Maksimum token/context sınırları olmalı
- Maksimum tool execution sayısı olmalı


==================================================
16. TESTLER
==================================================

En az:

- Agent orchestration tests
- Tool selection tests
- Tool execution tests
- RAG integration tests
- Error handling tests

oluştur.

Mock LLM kullanarak deterministic testler yaz.

Özellikle şu senaryoları test et:

1. Basit RAG sorusu
2. Tek tool gerektiren soru
3. Birden fazla tool gerektiren soru
4. Tool failure
5. RAG sonuçlarının yetersiz olması
6. Maximum iteration'a ulaşılması
7. Repository bulunamaması
8. Geçersiz tool argument


==================================================
17. API
==================================================

Agent'a soru sormak için bir endpoint oluştur.

Örneğin:

POST /api/agent/chat

Request:

{
    "repositoryId": "...",
    "conversationId": "...",
    "message": "Order oluşturulduğunda hangi servisler devreye giriyor?"
}

Response:

{
    "conversationId": "...",
    "answer": "...",
    "sources": [],
    "toolCalls": []
}

Mevcut API convention'larına uy.


==================================================
18. STREAMING
==================================================

İlk implementation'da streaming zorunlu değil.

Ancak architecture streaming'e ileride uygun olacak şekilde
tasarlanmalıdır.


==================================================
19. DOCUMENTATION
==================================================

Kod ile birlikte README/AGENT.md oluştur.

Dokümantasyonda mutlaka anlat:

1. Agent nedir?
2. RAG ile Agent arasındaki fark nedir?
3. Agent workflow
4. Tool calling nasıl çalışıyor?
5. Tool'lar nelerdir?
6. Agent nasıl karar veriyor?
7. Conversation context nasıl tutuluyor?
8. Error handling
9. Security
10. Örnek soru-cevap
11. Örnek tool calling flow
12. Local development
13. Configuration


==================================================
20. IMPLEMENTATION STRATEGY
==================================================

HEMEN TÜM KODU YAZMAYA BAŞLAMA.

Önce:

1. Repository'yi analiz et.
2. Mevcut backend'i analiz et.
3. Mevcut RAG implementation'ını analiz et.
4. Mevcut project structure'ı çıkar.
5. Agent'ın hangi mevcut servisleri kullanacağını belirle.
6. Bana kısa bir implementation planı göster.

Plan onaylanmadan büyük refactoring yapma.

Mevcut çalışan RAG sistemini bozma.

Yeni Agent katmanını mümkün olduğunca mevcut
abstraction'ların üzerine inşa et.

Kod üretirken her önemli architectural decision için
kısa bir açıklama ekle.

Amaç sadece çalışan bir Agent üretmek değil.

Amaç:

**Benim kodu okuyarak Agent architecture, RAG integration,
tool calling, orchestration ve LLM interaction kavramlarını
öğrenebilmem.**

Bu nedenle kodun okunabilir, modüler ve iyi dokümante edilmiş
olmasına özellikle dikkat et.