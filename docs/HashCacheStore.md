# HashCacheStore — Camada pública de cache

Arquivo fonte: `MiniRedis.Core/Cache/HashCacheStore.cs`

`HashCacheStore` é a ponte entre os **comandos do servidor** (`GET`, `SET`, `DELETE`...)
e o motor de hash table (`RedisDict`). Ela adiciona duas responsabilidades que o `RedisDict`
não tem: **thread-safety** e **expiração de itens (TTL)**.

---

## Posição na arquitetura

```
Cliente TCP
    │  (texto: "SET user:42 João 10")
    ▼
ClientSession
    │  parse + cria comando
    ▼
SetCommand / GetCommand / DeleteCommand / ...
    │  chama métodos de ICacheStore
    ▼
┌─────────────────────────────────────────┐
│           HashCacheStore                │  ← você está aqui
│  (thread-safety + TTL)                  │
└─────────────────────────────────────────┘
    │  chama métodos de RedisDict
    ▼
┌─────────────────────────────────────────┐
│              RedisDict                  │
│  (hash table dual com rehash incremental│
└─────────────────────────────────────────┘
```

---

## Variáveis

### `_dict` — `RedisDict` (privado, readonly)

```csharp
private readonly RedisDict _dict = new();
```

A hash table customizada que guarda todos os dados. Criada na inicialização e usada por toda a vida do `HashCacheStore`. Não é thread-safe sozinha — por isso existe o `_lock`.

---

### `_lock` — `ReaderWriterLockSlim` (privado, readonly)

```csharp
private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
```

O mecanismo de sincronização entre múltiplos clientes TCP que acessam o cache simultaneamente.

**Por que `ReaderWriterLockSlim` e não `lock`?**

O `lock` comum bloqueia **todo mundo** enquanto alguém segura o lock, inclusive outros leitores.
O `ReaderWriterLockSlim` permite **múltiplas leituras simultâneas** — apenas escritas são exclusivas:

```
Thread A: GET user:1   ──► EnterReadLock  ──► lê ──► ExitReadLock
Thread B: GET user:2   ──► EnterReadLock  ──► lê ──► ExitReadLock   (paralelo com A!)
Thread C: SET user:3   ──► (espera A e B) ──► EnterWriteLock ──► escreve ──► ExitWriteLock
Thread D: GET user:4   ──► (espera C) ──► EnterReadLock ──► lê ──► ExitReadLock
```

Para um servidor de cache onde GET é muito mais frequente que SET, isso é relevante.

**`LockRecursionPolicy.NoRecursion`**: o mesmo thread não pode pedir o lock duas vezes
(evita bugs onde código aninhado tenta adquirir o mesmo lock e trava).

---

## Métodos

### `Count` — propriedade pública

```csharp
public long Count { get { ... } }
```

Retorna o número total de entradas na tabela (soma de `ht0.Used + ht1.Used` do `RedisDict`).

```
Thread qualquer chama Count:
    │
    ▼
EnterReadLock()      ← múltiplos threads podem estar aqui ao mesmo tempo
    │
    ▼
_dict.Count          ← lê int, operação atômica no hardware, seguro
    │
    ▼
ExitReadLock()
    │
    ▼
retorna valor
```

---

### `Set(string key, string json, TimeSpan? ttl)` — público

Armazena um valor no cache, com TTL opcional.

**Fluxo:**

```
Set("user:42", "João", TimeSpan.FromMinutes(10))
        │
        ▼
   ttl tem valor?
     SIM → expiresAt = DateTimeOffset.UtcNow + 10min
     NÃO → expiresAt = null  (nunca expira)
        │
        ▼
   CacheItem item = new("João", expiresAt)
        │
        ▼
   EnterWriteLock()    ← exclusivo: ninguém mais lê ou escreve
        │
        ▼
   _dict.Set("user:42", item)
     ├─ chave já existe? → atualiza Value in-place
     └─ chave nova?      → insere, faz RehashStep se necessário
        │
        ▼
   ExitWriteLock()
```

O `CacheItem` é criado **antes** do write lock (cálculo da expiração não precisa ser protegido).
Isso minimiza o tempo que o lock é mantido.

---

### `Get(string key)` — público

Busca um valor. Se encontrar e estiver expirado, remove e retorna `null`.

**Fluxo:**

```
Get("user:42")
        │
        ▼
   EnterReadLock()    ← leitura: pode ser paralela com outros Gets
        │
        ▼
   _dict.TryGetValue("user:42", out item)
        │
        ▼
   ExitReadLock()     ← liberado ANTES de checar expiração
        │
        ├─ não encontrou ──────────────────────────────► return null
        │
        ▼
   item.IsExpired()?
        │
        ├─ NÃO ──────────────────────────────────────► return item.Value  ✓
        │
        └─ SIM (expirou)
                │
                ▼
           EnterWriteLock()    ← agora precisa de write para deletar
                │
                ▼
           _dict.Remove("user:42")
                │
                ▼
           ExitWriteLock()
                │
                ▼
           return null
```

**Ponto importante — dois locks separados:**

O `Get` usa o read lock para **buscar** e depois, se necessário, um write lock para **deletar**.
Entre os dois locks existe uma janela onde outro thread poderia deletar o mesmo item.
Isso é seguro porque `_dict.Remove()` retorna `false` quando a chave não existe — não causa erro,
só não faz nada. O cliente recebe `null` de qualquer forma.

Esse padrão se chama **expiração lazy**: o item não é removido proativamente num timer,
mas sim na próxima vez que alguém tentar lê-lo. Exatamente como o Redis faz por padrão.

---

### `Delete(string key)` — público

Remove uma chave diretamente, sem checar expiração.

```
Delete("user:42")
        │
        ▼
   EnterWriteLock()
        │
        ▼
   _dict.Remove("user:42")
     ├─ encontrou → remove da cadeia, ajusta ponteiros, Used--
     └─ não encontrou → return false (sem erro)
        │
        ▼
   ExitWriteLock()
```

---

### `Exists(string key)` — público

Verifica se uma chave existe **e não está expirada**. Segue a mesma lógica lazy de `Get`.

```
Exists("user:42")
        │
        ▼
   EnterReadLock()
        │
        ▼
   _dict.TryGetValue("user:42", out item)
        │
        ▼
   ExitReadLock()
        │
        ├─ não encontrou ──► return false
        │
        ▼
   item.IsExpired()?
        │
        ├─ NÃO ──► return true  ✓
        │
        └─ SIM
                │
                ▼
           EnterWriteLock()
                │
                ▼
           _dict.Remove("user:42")
                │
                ▼
           ExitWriteLock()
                │
                ▼
           return false
```

---

### `Clear()` — público

Descarta todas as entradas e recria a tabela do zero.

```
Clear()
    │
    ▼
EnterWriteLock()    ← exclusivo: congela leitores e escritores
    │
    ▼
_dict.Clear()
  → ht0 = Ht.Create(4)   (novo array vazio de 4 buckets)
  → ht1 = default         (descartado)
  → _rehashIdx = -1
    │
    ▼
ExitWriteLock()
```

---

### `GetAll()` — público

Retorna todos os itens do cache. Usado pelo `SnapshotService` para persistir o estado.

```
GetAll()
    │
    ▼
EnterReadLock()
    │
    ▼
_dict.ToList()
  → copia todos os itens de ht0 e ht1 para uma List<>
  → retorna a lista pronta
    │
    ▼
ExitReadLock()
    │
    ▼
return List<KeyValuePair<string, CacheItem>>
```

**Por que `ToList()` e não `yield return`?**

Se `GetAll()` retornasse um `IEnumerable` lazy (com `yield return`), o lock seria liberado
antes da iteração acontecer. O `SnapshotService` iteraria os dados **sem proteção**, e um
outro thread poderia modificar a tabela durante a serialização para JSON.

Ao retornar uma `List<>` já materializada, o lock é mantido apenas durante a cópia —
o tempo mais curto possível — e o caller pode iterar com segurança sem segurar lock nenhum.

---

## Padrão de uso dos locks — visão consolidada

| Método | Lock | Motivo |
|---|---|---|
| `Count` | Read | Leitura de int, não modifica |
| `Get` | Read → (Write se expirado) | Busca paralela; só escreve para limpar expirado |
| `Set` | Write | Modifica a tabela e pode triggerar rehash |
| `Delete` | Write | Modifica a tabela |
| `Exists` | Read → (Write se expirado) | Igual ao Get |
| `Clear` | Write | Substitui a tabela inteira |
| `GetAll` | Read | Copia sem modificar |

---

## Diagrama de threads concorrentes

```
t=0  Thread A (GET user:1) ──► EnterReadLock ─────────────────► ExitReadLock
t=0  Thread B (GET user:2) ──► EnterReadLock ─────────────────► ExitReadLock
t=0  Thread C (GET user:3) ──► EnterReadLock ─────────────────► ExitReadLock
                                     ▲ os 3 leem em paralelo

t=1  Thread D (SET user:4) ──────────────────────────────────► EnterWriteLock ──► ExitWriteLock
                                                                    ▲ espera A, B e C terminarem

t=2  Thread E (GET user:5) ──────────────────────────────────────────────────► EnterReadLock ──► ExitReadLock
                                                                                    ▲ espera D terminar
```

---

## Por que o CacheItem é imutável?

```csharp
public class CacheItem
{
    public string Value { get; }       // sem setter
    public DateTimeOffset? ExpiresAt { get; }  // sem setter
}
```

`Get()` captura o `item` sob read lock, mas checa `item.IsExpired()` **após** liberar o lock.
Isso só é seguro porque `CacheItem` é imutável — nenhum outro thread pode mudar `ExpiresAt`
depois que capturamos a referência. Se `CacheItem` fosse mutável, a verificação de expiração
precisaria estar dentro do lock.

---

## Fluxo de snapshot (integração com SnapshotService)

```
CacheRecoveryService (a cada 10s):
    │
    ▼
SnapshotService.Save()
    │
    ▼
hashCacheStore.GetAll()
    │  EnterReadLock
    │  _dict.ToList() → copia todos os itens
    │  ExitReadLock
    │
    ▼
Filtra itens expirados
    │
    ▼
Serializa para JSON
    │
    ▼
Grava snapshot.json
```

O lock é liberado antes da serialização JSON — o arquivo pode demorar para gravar, mas
a tabela de hash está livre para servir requests normalmente durante esse tempo.
