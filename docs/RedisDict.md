# RedisDict — Hash Table customizada estilo Redis

Arquivo fonte: `MiniRedis.Core/Cache/RedisDict.cs`

Este arquivo contém três tipos que trabalham juntos para formar a hash table:
`RedisDictEntry` → `Ht` (struct interna) → `RedisDict` (motor principal).

---

## 1. RedisDictEntry — nó da lista encadeada

Cada entrada armazenada na tabela é um objeto `RedisDictEntry`. Quando dois itens caem no mesmo bucket (colisão), eles formam uma lista encadeada através do campo `Next`.

```
┌─────────────────────────┐
│     RedisDictEntry      │
├─────────────────────────┤
│ Key   : string          │  → chave original ("user:42")
│ Hash  : uint            │  → hash cacheado (calculado uma vez, reutilizado no rehash)
│ Value : CacheItem       │  → valor armazenado
│ Next  : RedisDictEntry? │  → próxima entry no mesmo bucket (null = fim da cadeia)
└─────────────────────────┘
```

### Variáveis

| Campo | Tipo | Descrição |
|---|---|---|
| `Key` | `string` (readonly) | A chave do item. Nunca muda após criação. |
| `Hash` | `uint` (readonly) | Hash da chave calculado **uma vez** no momento da inserção. Reutilizado durante o rehash para evitar recalcular. |
| `Value` | `CacheItem` | O valor armazenado. Não é readonly — pode ser atualizado in-place quando a chave já existe. |
| `Next` | `RedisDictEntry?` | Ponteiro para o próximo nó na mesma cadeia. `null` indica o último elemento. |

### Por que cachear o Hash?

Durante o rehash, todos os itens de um bucket de `ht0` precisam ser relocados para `ht1`.
Se não cachearmos o hash, teríamos que recalcular `string.GetHashCode()` para cada item migrado.
Com o hash cacheado, a migração é puramente aritmética: `hash & ht1.SizeMask`.

### Construtor

```csharp
RedisDictEntry(string key, uint hash, CacheItem value)
```

Inicializa os três campos imutáveis. `Next` começa como `null` e é configurado
separadamente pelo `RedisDict` antes de inserir no bucket.

---

## 2. Ht (struct interna) — um array de buckets

`Ht` representa **metade** da hash table dual. `RedisDict` mantém dois: `_ht0` e `_ht1`.

```
┌───────────────────────────────────────────────────┐
│                      Ht                           │
├───────────────────────────────────────────────────┤
│ Buckets  : RedisDictEntry?[]  (tamanho = potência de 2) │
│ SizeMask : int                (= Buckets.Length - 1)    │
│ Used     : int                (entradas vivas neste Ht) │
└───────────────────────────────────────────────────┘
```

### Variáveis

| Campo | Tipo | Descrição |
|---|---|---|
| `Buckets` | `RedisDictEntry?[]` | Array de ponteiros para cabeças de cadeia. Cada posição é um bucket. |
| `SizeMask` | `int` | Sempre `Size - 1`. Usado em `hash & SizeMask` no lugar de `hash % Size`. Só funciona porque o tamanho é sempre potência de 2. |
| `Used` | `int` | Quantidade de entradas vivas **neste** array (não na tabela inteira). |
| `Size` (propriedade) | `int` | Retorna `Buckets.Length`. Derivado — não armazenado separadamente. |

### Por que SizeMask em vez de %?

```
// Lento: divisão inteira
bucket = hash % 8;   // instrução DIV — caro na CPU

// Rápido: AND bit a bit (funciona porque 8 = 2³)
bucket = hash & 7;   // instrução AND — um ciclo de clock

// Prova: 0b1011_0101 & 0b0000_0111 = 0b0000_0101 = 5
//        A máscara preserva só os últimos N bits, que é exatamente o que % potência-de-2 faz.
```

### Ht.Create(int size)

Único factory method. Aloca o array, calcula a máscara e zera `Used`.

```csharp
Ht.Create(8) → Buckets = new RedisDictEntry?[8], SizeMask = 7, Used = 0
```

---

## 3. RedisDict — motor principal

Mantém **dois** arrays `Ht` (`_ht0` e `_ht1`) para poder fazer o rehash incremental sem
travar o servidor enquanto migra os dados.

### Variáveis

| Campo | Tipo | Descrição |
|---|---|---|
| `InitialSize` | `const int = 4` | Tamanho do array ao criar a tabela. Mínimo de 4 garante que `NextPowerOfTwo` nunca retorne menos que isso. |
| `_ht0` | `Ht` | Array principal. Onde a maioria das operações acontece quando não há rehash em curso. |
| `_ht1` | `Ht` | Array de destino durante o rehash. Fica vazio (`default`) quando `_rehashIdx == -1`. |
| `_rehashIdx` | `int` | `-1` = tabela estática (sem rehash). `>= 0` = índice do próximo bucket de `_ht0` a ser migrado para `_ht1`. |
| `Count` | `int` (propriedade) | `_ht0.Used + _ht1.Used` — soma os dois porque durante o rehash as entradas estão divididas entre os dois arrays. |

### Layout de memória — estado estático (sem rehash)

```
_rehashIdx = -1
_ht1 = default (vazio)

_ht0:
  Buckets[0] ──► Entry("session:1", hash=0xA3, ...) ──► null
  Buckets[1] ──► null
  Buckets[2] ──► Entry("user:42", hash=0xF2, ...)
                    └─► Entry("user:99", hash=0x1A, ...)  ← colisão! mesma cadeia
                           └─► null
  Buckets[3] ──► Entry("token:X", hash=0x7C, ...) ──► null
```

### Layout de memória — durante o rehash (ht0 → ht1)

```
_rehashIdx = 2   (buckets 0 e 1 já foram migrados)

_ht0 (original, 4 buckets, sendo esvaziado):
  Buckets[0] ──► null          ← já migrado
  Buckets[1] ──► null          ← já migrado
  Buckets[2] ──► Entry("user:42") ──► Entry("user:99") ──► null   ← ainda aqui
  Buckets[3] ──► Entry("token:X") ──► null                         ← ainda aqui

_ht1 (destino, 8 buckets, sendo preenchido):
  Buckets[0] ──► null
  Buckets[1] ──► Entry("session:1") ──► null   ← veio do ht0[0]
  Buckets[4] ──► null                           ← onde "user:42" irá quando ht0[2] for migrado
  ...
```

---

## Métodos

### `ComputeHash(string key)` — privado estático

```csharp
unchecked((uint)string.GetHashCode(key, StringComparison.Ordinal))
```

- **Ordinal**: comparação byte a byte, sem regras de cultura. É o mais rápido.
- **`unchecked`**: converte `int` (que pode ser negativo) para `uint` sem lançar exceção.
- Nunca persistimos o hash — só a chave. Então a randomização por processo do .NET não é problema.

---

### `BucketOf(uint hash, int sizeMask)` — privado estático

```csharp
(int)(hash & (uint)sizeMask)
```

Converte hash para índice de bucket. Um AND bit a bit é equivalente a `% Size` quando `Size` é potência de 2.

---

### `TryGetValue(string key, out CacheItem value)` — público

**Fluxo:**

```
TryGetValue("user:42")
        │
        ▼
   ComputeHash("user:42")  →  hash = 0xF2
        │
        ▼
   BucketOf(0xF2, ht0.SizeMask=3)  →  idx = 2
        │
        ▼
   Percorre cadeia em ht0.Buckets[2]:
        │
        ├─ e.Hash == 0xF2 && e.Key == "user:42"? ── SIM ──► value = e.Value; return true
        │
        └─ NÃO → próximo nó → null? ── SIM ──► tenta ht1 (se rehashando)
                                                    │
                                                    ├─ _rehashIdx < 0? ──► value = default; return false
                                                    │
                                                    └─ Percorre cadeia em ht1.Buckets[idx1]
                                                            │
                                                            ├─ achou ──► return true
                                                            └─ não achou ──► return false
```

- Compara `Hash` (uint, O(1)) **antes** de `Key` (string, O(n)). Evita comparações de string caras para entries com hash diferente.
- Busca em `ht1` só acontece se `_rehashIdx >= 0`.

---

### `Set(string key, CacheItem value)` — público

**Fluxo:**

```
Set("user:42", item)
        │
        ▼
   _rehashIdx >= 0? ──► SIM ──► RehashStep()  (migra 1 bucket antes de inserir)
        │
        ▼
   ComputeHash("user:42")  →  hash
        │
        ▼
   FindEntry(hash, key)  →  entry já existe?
        │
        ├─ SIM ──► entry.Value = item; return   (atualização in-place, sem realocar)
        │
        └─ NÃO
              │
              ▼
         Escolhe array de destino:
           _rehashIdx >= 0  →  ref _ht1   (durante rehash, novas entradas vão para ht1)
           _rehashIdx  < 0  →  ref _ht0   (normal)
              │
              ▼
         Head insertion no bucket (O(1)):
           newEntry.Next = target.Buckets[idx]
           target.Buckets[idx] = newEntry
           target.Used++
              │
              ▼
         _ht0.Used >= _ht0.Size?  ──► SIM ──► StartExpand()
```

> **Por que entradas novas vão para `ht1` durante o rehash?**
> Se fossem para `ht0`, poderíamos inserir em um bucket que ainda não foi migrado,
> e depois migrá-lo para `ht1` novamente — correto, mas o rehash nunca terminaria
> se continuarmos inserindo no mesmo bucket que está sendo processado.
> Indo para `ht1`, `ht0` só perde entradas, nunca ganha → o rehash sempre converge.

---

### `Remove(string key)` — público

**Fluxo:**

```
Remove("user:42")
        │
        ▼
   _rehashIdx >= 0? ──► RehashStep()
        │
        ▼
   TryRemoveFrom(ref _ht0, hash, key)
        │
        ├─ SIM ──► TryFinishRehash(); return true
        │
        └─ NÃO
              │
              ▼
         _rehashIdx >= 0?
              │
              ├─ NÃO ──► return false  (não está em nenhum array)
              │
              └─ SIM ──► TryRemoveFrom(ref _ht1, hash, key)
                              │
                              ├─ SIM ──► TryFinishRehash(); return true
                              └─ NÃO ──► return false
```

---

### `TryRemoveFrom(ref Ht ht, uint hash, string key)` — privado

Remove da cadeia encadeada ajustando os ponteiros `prev` e `Next`:

```
Antes da remoção de "user:42":
  Buckets[2] ──► Entry("token:X") ──► Entry("user:42") ──► Entry("user:99") ──► null
                     [prev]               [e a remover]

Depois:
  Buckets[2] ──► Entry("token:X") ──► Entry("user:99") ──► null
                     [prev]
                 prev.Next = e.Next   ←  cirurgia no ponteiro
```

Se o nó a remover for o primeiro da cadeia (`prev == null`), o bucket aponta diretamente para `e.Next`.

---

### `RehashStep()` — privado

Migra **um bucket inteiro** de `ht0` para `ht1`. Chamado uma vez por operação de escrita.

```
Estado: _rehashIdx = 2

Passo 1: pula buckets vazios
  ht0.Buckets[2] != null? SIM → para aqui

Passo 2: pega a cadeia toda do bucket
  cadeia = ht0.Buckets[2]   →  Entry("user:42") → Entry("user:99") → null
  ht0.Buckets[2] = null     →  bucket esvaziado em ht0

Passo 3: para cada entry na cadeia:
  Entry("user:42"):
    newIdx = hash_A & ht1.SizeMask  →  4
    entry.Next = ht1.Buckets[4]     →  null (bucket vazio)
    ht1.Buckets[4] = entry
    ht0.Used--; ht1.Used++

  Entry("user:99"):
    newIdx = hash_B & ht1.SizeMask  →  4   (mesma cadeia? diferente? depende do hash)
    entry.Next = ht1.Buckets[4]     →  Entry("user:42")   ← head insertion
    ht1.Buckets[4] = entry
    ht0.Used--; ht1.Used++

Passo 4: _rehashIdx++ → 3

Passo 5: TryFinishRehash() → ht0.Used == 0? → não ainda
```

> O custo é proporcional ao número de entradas **no bucket migrado**, não ao tamanho total da tabela.
> Buckets vazios são pulados no loop `while` sem custo.

---

### `StartExpand()` / `FinishRehash()` / `TryFinishRehash()` — privados

```
StartExpand():
  nova_capacidade = NextPowerOfTwo(ht0.Used * 2)
  ht1 = Ht.Create(nova_capacidade)
  _rehashIdx = 0   ← sinaliza início do rehash

TryFinishRehash():
  if _rehashIdx >= 0 && ht0.Used == 0:
    FinishRehash()

FinishRehash():
  ht0 = ht1        ← ht1 vira o novo ht0
  ht1 = default    ← ht1 zerado (null)
  _rehashIdx = -1  ← volta ao estado estático
```

**Ciclo de vida completo do rehash:**

```
[Tabela cheia]
    │
    ▼
StartExpand()  →  ht1 alocado, _rehashIdx = 0
    │
    ▼
 Set()/Remove() chamados repetidamente
    │  cada um chama RehashStep() que migra 1 bucket
    ▼
ht0.Used chega a 0
    │
    ▼
FinishRehash()  →  ht0 = ht1, ht1 = null, _rehashIdx = -1
    │
    ▼
[Tabela nova, maior, pronta]
```

---

### `NextPowerOfTwo(int n)` — privado estático

Calcula a próxima potência de 2 maior ou igual a `n` usando bit manipulation:

```
Exemplo: n = 5

n-- → 4            = 0b00000100
n |= n >> 1 → 6   = 0b00000110
n |= n >> 2 → 7   = 0b00000111
n |= n >> 4 → 7   = 0b00000111
n |= n >> 8 → 7   = 0b00000111
n |= n >> 16 → 7  = 0b00000111
n++ → 8            = 0b00001000  ✓

Tabela de crescimento:
  4 entradas → expande para 8
  8 entradas → expande para 16
  16 entradas → expande para 32
  ...
```

O truque: `n--` para "recuar um", depois OR sucessivos "espalham" o bit mais significativo
para todos os bits abaixo dele, formando uma sequência de 1s. `n++` sobe para a potência de 2 exata.

---

### `ToList()` — público

Coleta todas as entradas de `ht0` e `ht1` em uma `List<>` e retorna.

```
ToList():
  lista = []

  para cada bucket em ht0.Buckets:
    para cada entry na cadeia:
      lista.Add( KeyValuePair(entry.Key, entry.Value) )

  se _rehashIdx >= 0:   ← tabela em rehash? coleta ht1 também
    para cada bucket em ht1.Buckets:
      para cada entry na cadeia:
        lista.Add( KeyValuePair(entry.Key, entry.Value) )

  return lista
```

Não usa `yield return` de propósito — coleta tudo de uma vez enquanto o caller ainda segura o lock.
Se fosse `yield return`, o lock seria liberado antes de iterar, e outro thread poderia modificar a tabela durante a iteração.

---

### `Clear()` — público

```csharp
_ht0 = Ht.Create(InitialSize);  // recria com 4 buckets
_ht1 = default;                  // descarta ht1
_rehashIdx = -1;                 // estado estático
```

Descarta tudo e começa do zero. O GC coleta os `RedisDictEntry` antigos.

---

## Fluxo completo: inserção de 5 chaves do zero

```
Estado inicial:
  ht0: [null, null, null, null]  (4 buckets, Used=0)
  ht1: vazio
  _rehashIdx: -1

Set("a") → idx = hash_a & 3 = 1
  ht0: [null, A, null, null]  Used=1

Set("b") → idx = 0
  ht0: [B, A, null, null]  Used=2

Set("c") → idx = 1  (colisão com "a"!)
  ht0: [B, C→A, null, null]  Used=3

Set("d") → idx = 3
  ht0: [B, C→A, null, D]  Used=4

Set("e"):
  ← Used(4) >= Size(4)? SIM → StartExpand()
  ht1 = Ht.Create(8)  (NextPowerOfTwo(4*2) = 8)
  _rehashIdx = 0

  Agora insere "e":
    RehashStep(): migra bucket 0 → "b" vai para ht1
    _rehashIdx = 1
    "e" inserida em ht1 (novas entradas vão para ht1)

Estado após Set("e"):
  ht0: [null, C→A, null, D]  Used=3   ← bucket 0 já migrado
  ht1: [null, null, B, null, null, E, null, null]  (exemplo)  Used=2
  _rehashIdx: 1
```

---

## Resumo das garantias

| Propriedade | Como é garantida |
|---|---|
| O(1) amortizado para Get/Set/Delete | Rehash distribuído, 1 bucket por operação |
| Sem bloqueio longo | Nunca migra tudo de uma vez |
| Sem perda de dados durante rehash | Busca em ht0 E ht1 simultaneamente |
| Rehash sempre converge | Novas entradas vão para ht1, ht0 só encolhe |
| Sem colisão de bucket | Tamanho é potência de 2, AND garante resultado no intervalo [0, Size-1] |
