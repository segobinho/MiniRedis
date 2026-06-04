# Parte 5 — Integração Cache + Snapshot

**Branch:** `feature/cache-recovery`

---

## O que muda da Parte 4 para a 5

Na Parte 4 o snapshot era manual e pontual:

| Parte 4 | Parte 5 |
|---|---|
| `Save()` só no shutdown | `Save()` periódico automático |
| `Load()` no `Program.cs` | `Load()` encapsulado no serviço |
| Snapshot como utilitário | Snapshot como comportamento contínuo |
| Cache frágil a crashes | Cache resiliente a quedas inesperadas |

Essa diferença separa um exercício de um sistema real.

---

## Conceito central: Ciclo de vida da aplicação

Três perguntas que precisam de resposta:

1. Quando **carregar** o snapshot?
2. Quando **salvar** o snapshot?
3. **Quem** controla isso?

**Resposta:** uma camada de orquestração — nem o cache, nem o snapshot.

---

## CacheRecoveryService

Classe criada nessa etapa com responsabilidade única: coordenar cache e snapshot ao longo da vida da aplicação.

```
CacheRecoveryService
├── Conhece: ISnapshotService
├── Conhece: intervalo de tempo (TimeSpan)
├── NÃO conhece: comandos
├── NÃO conhece: rede / TCP
└── NÃO conhece: console / UI
```

### Construtor

```csharp
public CacheRecoveryService(ISnapshotService snapshotService, TimeSpan interval)
```

O intervalo entra via construtor — nunca hardcoded. Isso permite testar com `TimeSpan.FromSeconds(1)` e rodar em produção com `TimeSpan.FromMinutes(5)`.

---

## Fluxo completo

```
App inicia
  └─ CacheRecoveryService.Start()
       ├─ SnapshotService.Load()      ← restaura estado anterior
       └─ Task em background inicia
            └─ loop:
                 ├─ Task.Delay(intervalo)
                 ├─ SnapshotService.Save()
                 └─ repete enquanto !token.IsCancellationRequested

App encerra (CTRL+C ou Console.ReadLine)
  └─ CacheRecoveryService.Stop()
       ├─ _cts.Cancel()              ← para o loop
       ├─ _backgroundTask.Wait()     ← espera terminar
       └─ SnapshotService.Save()     ← último save garantido
```

---

## Implementação

### Start()

```csharp
public void Start()
{
    try
    {
        _snapshotService.Load();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Snapshot] Load failed: {ex.Message}");
        // nunca derruba a app — continua sem dados anteriores
    }

    _cts = new CancellationTokenSource();
    _backgroundTask = Task.Run(() => RunSnapshotLoop(_cts.Token));
}
```

**Regra:** falha no `Load()` nunca derruba a aplicação. O servidor sobe vazio e continua funcionando.

### RunSnapshotLoop()

```csharp
private async Task RunSnapshotLoop(CancellationToken token)
{
    while (!token.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(_interval, token);  // espera cancelável
            _snapshotService.Save();
        }
        catch (TaskCanceledException)
        {
            // shutdown normal — sai do loop silenciosamente
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Snapshot] Save failed: {ex.Message}");
            // falha no save não derruba o loop
        }
    }
}
```

`Task.Delay(_interval, token)` é a chave: ele espera o intervalo mas cancela imediatamente quando o token for cancelado — sem deixar o shutdown travado esperando o próximo ciclo.

### Stop()

```csharp
public void Stop()
{
    _cts?.Cancel();

    try { _backgroundTask?.Wait(); }
    catch { }

    try { _snapshotService.Save(); }   // último save garantido
    catch (Exception ex)
    {
        Console.WriteLine($"[Snapshot] Final save failed: {ex.Message}");
    }
}
```

O último `Save()` após cancelar o loop garante que dados escritos nos segundos finais antes do shutdown não sejam perdidos.

---

## ISnapshotService

Interface extraída para desacoplar `CacheRecoveryService` da implementação concreta:

```csharp
public interface ISnapshotService
{
    void Save();
    void Load();
}
```

`CacheRecoveryService` depende da interface — nunca da classe concreta `SnapshotService`. Isso permite trocar a implementação (ex: serializar em binário em vez de JSON) sem alterar o serviço de ciclo de vida.

---

## SnapshotService

Responsável por serializar e desserializar o cache em disco.

**Save:** itera todos os itens do cache, descarta os expirados, serializa o restante em JSON com `System.Text.Json`.

**Load:** lê o arquivo JSON, descartar itens já expirados (TTL expirou enquanto o servidor estava parado), e restaura os válidos via `_cache.Set(key, value, ttl)`.

```
snapshot.json
{
  "CreatedAt": "2026-06-04T12:00:00Z",
  "Items": [
    { "Key": "usuario:1", "Value": "alice", "ExpiresAt": null },
    { "Key": "sessao:abc", "Value": "token_xyz", "ExpiresAt": "2026-06-04T13:00:00Z" }
  ]
}
```

---

## Thread safety — alerta

A partir desta etapa existem dois fluxos concorrentes acessando o cache:

- **Commands** (via `CommandFactory`) → escrevem no cache
- **SnapshotService** (via loop em background) → lê o cache

O `SnapshotService.Save()` chama `_cache.GetAll()`. Se o cache não for thread-safe, esse acesso concorrente pode corromper dados.

**Soluções possíveis:**
- `lock` em torno de leituras/escritas no cache
- `ConcurrentDictionary` como estrutura interna
- Snapshot operar sobre uma **cópia** (`ToList()` / `ToArray()`) para não bloquear o cache durante a serialização

Esse tema é aprofundado na Parte 7 (concorrência).

---

## Uso no Program.cs

```csharp
var snapshotService = new SnapshotService(cache, snapshotPath);
var cacheRecovery = new CacheRecoveryService(snapshotService, TimeSpan.FromSeconds(10));

cacheRecovery.Start();   // load + inicia loop

// ... servidor rodando ...

cacheRecovery.Stop();    // cancela loop + último save
```

`Program.cs` orquestra — ele não faz `Load()` ou `Save()` diretamente. Toda lógica de ciclo de vida fica encapsulada no serviço.

---

## O que NÃO fazer

- Cache chamar snapshot diretamente
- Snapshot controlar o próprio loop
- `Program.cs` virar um "deus" com lógica de negócio
- IO (arquivo) dentro do core de cache
- `Thread.Sleep` no lugar de `Task.Delay`
- Hardcode de intervalo ou caminho de arquivo

---

## Checklist

- [x] Load automático no startup
- [x] Snapshot periódico via `Task` + `CancellationToken`
- [x] Cancelamento limpo sem travar shutdown
- [x] Último save garantido no `Stop()`
- [x] Falha no load não derruba a aplicação
- [x] Cache isolado do snapshot (via interface)
- [x] Intervalo configurável via construtor
- [x] `ISnapshotService` extraída para desacoplamento
