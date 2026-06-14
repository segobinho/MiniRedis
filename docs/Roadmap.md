# MiniRedis — Roadmap para um Redis de verdade

## Estado atual

| Feature | Status |
|---|---|
| Hash table customizada com rehash incremental | ✅ |
| TTL / expiração lazy | ✅ |
| Persistência em JSON (snapshot a cada 10s) | ✅ |
| Servidor TCP na porta 6379 | ✅ |
| Comandos: GET, SET, DELETE, CLEAR | ✅ |
| Thread-safety com ReaderWriterLockSlim | ✅ |

---

## Tier 1 — RESP Protocol (bloqueador principal)

> Sem isso, nenhum cliente Redis real consegue conectar (`redis-cli`, `StackExchange.Redis`, etc.)

Hoje o servidor fala texto plano. Redis usa RESP (Redis Serialization Protocol):

| Tipo | Prefixo | Exemplo |
|---|---|---|
| Simple String | `+` | `+OK\r\n` |
| Error | `-` | `-ERR unknown command\r\n` |
| Integer | `:` | `:1000\r\n` |
| Bulk String | `$` | `$6\r\nfoobar\r\n` |
| Null | `$-1\r\n` | (GET de chave inexistente) |
| Array | `*` | `*3\r\n$3\r\nSET\r\n$3\r\nfoo\r\n$3\r\nbar\r\n` |

**O que mudar:** reescrever o parser em `ClientSession.cs` para ler arrays RESP em vez de texto, e formatar todas as respostas no padrão RESP.

- [ ] Parser RESP no `ClientSession`
- [ ] Respostas formatadas em RESP em todos os comandos
- [ ] Testar com `redis-cli -p 6379`

---

## Tier 2 — Comandos de string faltando

| Comando | O que faz | Prioridade |
|---|---|---|
| `PING` | Responde `PONG` — todo cliente testa primeiro | Alta |
| `EXISTS key` | Verifica se a chave existe | Alta |
| `TTL key` | Retorna segundos restantes de vida | Alta |
| `PTTL key` | Retorna milissegundos restantes de vida | Alta |
| `EXPIRE key s` | Define TTL em chave já existente (segundos) | Alta |
| `PEXPIRE key ms` | Define TTL em milissegundos | Alta |
| `PERSIST key` | Remove o TTL de uma chave | Média |
| `TYPE key` | Retorna o tipo do valor (`string`, `list`, etc.) | Média |
| `RENAME key newkey` | Renomeia uma chave | Média |
| `KEYS pattern` | Lista chaves por padrão (`KEYS user:*`) | Média |
| `SCAN cursor` | Iteração incremental, não bloqueia o servidor | Média |
| `INCR key` | Incrementa inteiro armazenado em 1 | Alta |
| `INCRBY key n` | Incrementa por N | Alta |
| `DECR key` | Decrementa inteiro em 1 | Alta |
| `DECRBY key n` | Decrementa por N | Alta |
| `APPEND key value` | Concatena string ao valor existente | Baixa |
| `STRLEN key` | Retorna tamanho do valor | Baixa |
| `MGET key [key...]` | Lê múltiplas chaves de uma vez | Média |
| `MSET key val [...]` | Escreve múltiplas chaves de uma vez | Média |
| `GETSET key value` | Retorna valor antigo e seta novo | Baixa |
| `INFO` | Estatísticas do servidor (memória, clientes, etc.) | Média |
| `SELECT db` | Muda de banco (0–15) | Baixa |
| `DBSIZE` | Quantidade de chaves no banco atual | Baixa |

- [ ] Implementar comandos de alta prioridade
- [ ] Implementar comandos de média prioridade
- [ ] Implementar comandos de baixa prioridade

---

## Tier 3 — Expiry ativo (memory leak atual)

Hoje chaves expiradas ficam na memória para sempre se nunca forem lidas.
Real Redis roda um scanner em background:

```
A cada 100ms:
  sorteia 20 chaves aleatórias
  remove as expiradas
  se > 25% estavam expiradas → repete imediatamente
```

**O que mudar:** adicionar um loop periódico no `CacheRecoveryService` que varre
um subset de chaves por ciclo e remove as expiradas.

- [ ] Background scanner de expiração em `CacheRecoveryService`
- [ ] Expor contador de chaves expiradas no futuro `INFO`

---

## Tier 4 — Estruturas de dados

O poder do Redis vem das estruturas nativas. Cada uma requer um novo tipo de valor
no lugar do `string` atual em `CacheItem`.

### Hash
Campos nomeados dentro de uma chave. Ex: `user:42 → {nome: "João", idade: 30}`

| Comando | Descrição |
|---|---|
| `HSET key field value` | Define um campo |
| `HGET key field` | Lê um campo |
| `HMSET key f v [f v...]` | Define múltiplos campos |
| `HGETALL key` | Retorna todos os campos e valores |
| `HDEL key field` | Remove um campo |
| `HEXISTS key field` | Verifica se campo existe |
| `HKEYS key` | Lista todos os campos |
| `HVALS key` | Lista todos os valores |
| `HLEN key` | Quantidade de campos |

- [ ] Implementar tipo Hash
- [ ] Implementar comandos H*

### List
Lista duplamente encadeada. Usada para filas, histórico, feeds.

| Comando | Descrição |
|---|---|
| `LPUSH key value` | Insere no início |
| `RPUSH key value` | Insere no fim |
| `LPOP key` | Remove e retorna do início |
| `RPOP key` | Remove e retorna do fim |
| `LRANGE key start end` | Retorna elementos no intervalo |
| `LLEN key` | Tamanho da lista |
| `LINDEX key idx` | Elemento por índice |

- [ ] Implementar tipo List
- [ ] Implementar comandos L*

### Set
Conjunto sem ordem, sem duplicatas. Suporta operações de conjunto.

| Comando | Descrição |
|---|---|
| `SADD key member` | Adiciona elemento |
| `SMEMBERS key` | Retorna todos os elementos |
| `SREM key member` | Remove elemento |
| `SCARD key` | Quantidade de elementos |
| `SISMEMBER key member` | Verifica se elemento existe |
| `SUNION key [key...]` | União de sets |
| `SINTER key [key...]` | Interseção de sets |
| `SDIFF key [key...]` | Diferença de sets |

- [ ] Implementar tipo Set
- [ ] Implementar comandos S*

### Sorted Set
Set com score numérico por elemento. Usado para rankings e leaderboards.

| Comando | Descrição |
|---|---|
| `ZADD key score member` | Adiciona com score |
| `ZRANGE key start end` | Elementos por índice (ordem crescente) |
| `ZRANK key member` | Posição do elemento no ranking |
| `ZSCORE key member` | Score de um elemento |
| `ZREM key member` | Remove elemento |
| `ZCARD key` | Quantidade de elementos |

- [ ] Implementar tipo Sorted Set (skiplist ou heap)
- [ ] Implementar comandos Z*

---

## Tier 5 — Recursos avançados

### Pub/Sub
Mensageria em tempo real — publicadores e assinantes de canais.

| Comando | Descrição |
|---|---|
| `SUBSCRIBE channel` | Assina um canal |
| `UNSUBSCRIBE channel` | Cancela assinatura |
| `PUBLISH channel message` | Publica mensagem no canal |

- [ ] Implementar gerenciador de canais e assinaturas
- [ ] Modo de sessão `SUBSCRIBE` (sessão bloqueada aguardando mensagens)

### Transactions
Bloco atômico de comandos — todos executam ou nenhum.

| Comando | Descrição |
|---|---|
| `MULTI` | Inicia bloco de transação |
| `EXEC` | Executa todos os comandos do bloco |
| `DISCARD` | Descarta o bloco |
| `WATCH key` | Aborta EXEC se a chave mudar antes do EXEC |

- [ ] Estado de transação por sessão de cliente
- [ ] Fila de comandos pendentes durante MULTI
- [ ] Implementar WATCH com detecção de conflito

### AOF — Append-Only File
Mais durável que snapshots: persiste cada write no arquivo em tempo real.

```
Cada SET/DEL/EXPIRE → acrescenta linha no aof.log
Na reinicialização   → replay do aof.log reconstrói o estado
```

- [ ] Escrever no arquivo AOF a cada comando de escrita
- [ ] Loader de AOF na inicialização
- [ ] Configuração: `appendonly yes/no`, `appendfsync always/everysec/no`

### Políticas de Eviction
Quando memória cheia, decide quais chaves remover.

| Política | Comportamento |
|---|---|
| `noeviction` | Retorna erro quando cheio (padrão atual implícito) |
| `allkeys-lru` | Remove a chave menos recentemente usada |
| `allkeys-lfu` | Remove a chave menos frequentemente usada |
| `volatile-lru` | LRU só em chaves com TTL |
| `allkeys-random` | Remove chave aleatória |

- [ ] Rastrear `last_access` por entrada
- [ ] Configuração `maxmemory` e `maxmemory-policy`
- [ ] Verificar após cada Set se limite foi atingido

### Autenticação

- [ ] Comando `AUTH password`
- [ ] Configuração `requirepass`
- [ ] Bloquear comandos antes do AUTH em sessões não autenticadas

---

## Tier 6 — Produção (longo prazo)

| Feature | Descrição | Complexidade |
|---|---|---|
| **Replicação** | Master → Réplicas com sincronização | Muito alta |
| **Cluster** | Sharding automático entre nós | Muito alta |
| **Pipelining** | Processar múltiplos comandos num pacote TCP | Média |
| **Lua scripting** | `EVAL` — scripts atômicos no servidor | Alta |
| **Keyspace notifications** | Pub/Sub de eventos internos (expiração, escrita) | Alta |
| **Múltiplos bancos** | 16 bancos isolados (SELECT 0–15) | Baixa |
| **CONFIG GET/SET** | Leitura e ajuste de configuração em runtime | Média |
| **OBJECT ENCODING** | Otimizações de encoding por tamanho (ziplist, etc.) | Alta |

---

## Sequência recomendada

```
AGORA              CURTO PRAZO            MÉDIO PRAZO          LONGO PRAZO
  │                    │                      │                     │
  ▼                    ▼                      ▼                     ▼

RESP Protocol  →  PING + TTL/EXPIRE  →  Hash + List + Set  →  Pub/Sub
                  + INCR + KEYS          + Expiry ativo        + MULTI/EXEC
                  + INFO                 + SCAN                + AOF
                                                               + AUTH
```

> **Regra de ouro:** implemente o RESP primeiro.
> Sem ele, todos os outros recursos ficam isolados do ecossistema Redis
> e você não consegue testar com ferramentas reais como `redis-cli`.
