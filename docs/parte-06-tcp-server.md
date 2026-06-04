# Parte 6 — Servidor TCP

**Branch:** `feature/tcp-server`

---

## O que muda conceitualmente

| Antes | Agora |
|---|---|
| Tudo local (console) | Clientes externos via rede |
| Um único usuário | Múltiplas conexões simultâneas |
| Fluxo síncrono simples | Entrada/saída via socket |
| `Console.ReadLine()` | `StreamReader` sobre `NetworkStream` |

Essa etapa transforma o MiniRedis de um programa de terminal em um servidor de verdade — conectável via `redis-cli`, `telnet` ou qualquer cliente TCP.

---

## Arquitetura em camadas

```
Cliente TCP (redis-cli / telnet / qualquer cliente)
        │
        │  TCP na porta 6379
        ▼
┌─────────────────────┐
│    TcpRedisServer   │  abre porta, aceita conexões
└─────────┬───────────┘
          │  1 Task por cliente
          ▼
┌─────────────────────┐
│    ClientSession    │  lê linhas, envia respostas
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│   CommandParser     │  string → tokens
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│   CommandFactory    │  tokens → ICommand
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│     ICacheStore     │  estado compartilhado
└─────────────────────┘
```

Cada camada faz uma coisa só. Nenhuma camada superior conhece detalhes de rede; nenhuma camada inferior conhece TCP.

---

## TcpRedisServer

Responsabilidade: abrir a porta, aceitar conexões e delegar cada cliente para uma `ClientSession`.

```csharp
public class TcpRedisServer
{
    private readonly int _port;
    private readonly Func<ClientSession> _sessionFactory;
    private TcpListener? _listener;

    public async Task StartAsync(CancellationToken token)
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();

        while (!token.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(token);

            // cada cliente roda em background — servidor não bloqueia
            _ = Task.Run(() =>
            {
                var session = _sessionFactory();
                return session.HandleAsync(client, token);
            }, token);
        }
    }

    public void Stop() => _listener?.Stop();
}
```

**Pontos-chave:**
- `IPAddress.Any` — aceita conexões de qualquer interface de rede
- `AcceptTcpClientAsync(token)` — espera conexões sem bloquear a thread
- `Task.Run` por cliente — servidor continua aceitando enquanto atende clientes
- `_sessionFactory` via `Func<ClientSession>` — factory injetada, não instanciada aqui

**O que NÃO fazer aqui:**
- Ler comandos
- Executar comandos
- Conhecer o cache

---

## ClientSession

Responsabilidade: gerenciar o ciclo de vida de **um** cliente conectado — ler, processar, responder, encerrar.

```csharp
public class ClientSession
{
    private readonly CommandParser _parser;
    private readonly CommandFactory _factory;

    public async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        await writer.WriteLineAsync("MiniRedis TCP ready");

        while (!token.IsCancellationRequested && client.Connected)
        {
            var line = await reader.ReadLineAsync();

            if (line == null)
                break;  // cliente desconectou

            try
            {
                var tokens = _parser.Parse(line);
                var command = _factory.Create(tokens);
                var result = command.Execute();
                await writer.WriteLineAsync(result);
            }
            catch (Exception ex)
            {
                await writer.WriteLineAsync($"ERR {ex.Message}");
            }
        }
    }
}
```

**Pontos-chave:**
- `AutoFlush = true` — resposta enviada imediatamente, sem buffer manual
- `ReadLineAsync()` retorna `null` quando o cliente fecha a conexão — tratado com `break`
- Exceção no comando → responde `ERR <mensagem>` e **continua** o loop (não derruba a sessão)
- `using` nos streams — recursos liberados automaticamente ao sair do método

---

## Protocolo (versão simples)

Nesta etapa o protocolo é texto puro, uma linha por comando:

```
→  SET usuario alice
←  OK

→  GET usuario
←  alice

→  DEL usuario
←  1

→  GET usuario
←  (nil)
```

Sem binário, sem RESP (protocolo real do Redis). Simplicidade total — o foco é na estrutura, não no protocolo.

---

## Loop de leitura por cliente

```
cliente envia linha
        ↓
 ReadLineAsync() retorna string
        ↓
 CommandParser.Parse(line) → string[]
        ↓
 CommandFactory.Create(tokens) → ICommand
        ↓
 command.Execute() → string
        ↓
 WriteLineAsync(result)
        ↓
repete até: cliente desconectar | token cancelado | exceção de IO
```

---

## Tratamento de desconexão

| Situação | Comportamento |
|---|---|
| Cliente fecha normalmente | `ReadLineAsync()` retorna `null` → `break` |
| Erro de IO no socket | `Exception` capturada → sessão termina |
| Server encerrado (CTRL+C) | `token.IsCancellationRequested` → loop para |
| Erro em um comando | `ERR <msg>` enviado → **loop continua** |

**Regra fundamental:** cliente cai → sessão termina → **servidor e outros clientes continuam**.

---

## Concorrência

Cada cliente roda em seu próprio `Task`. O cache é um singleton compartilhado por todas as sessões:

```
ClientSession A  ──┐
ClientSession B  ──┼──► ICacheStore (compartilhado)
ClientSession C  ──┘
```

Por enquanto o foco é na estrutura. Thread safety completa é endereçada na Parte 7.

---

## Injeção de dependências

O `Program.cs` monta tudo e injeta via construtor:

```csharp
var parser = new CommandParser();
var factory = new CommandFactory();
CoreCommandRegistration.RegisterAll(factory, cache);

var server = new TcpRedisServer(
    port: 6379,
    sessionFactory: () => new ClientSession(parser, factory)
);
```

`parser` e `factory` são compartilhados entre todas as sessões — thread-safe por não terem estado mutável. O `cache` está dentro dos comandos registrados na `factory`.

---

## Integração com CacheRecoveryService

O servidor TCP convive com o ciclo de vida de snapshot da Parte 5:

```csharp
cacheRecovery.Start();          // load + inicia loop periódico

_ = server.StartAsync(cts.Token);  // servidor em background

Console.ReadLine();             // mantém app viva

cts.Cancel();
cacheRecovery.Stop();           // último save
server.Stop();
```

O servidor TCP não conhece o snapshot — eles coexistem de forma independente, ambos orquestrados pelo `Program.cs`.

---

## Testando manualmente

Com o servidor rodando:

```
telnet localhost 6379
```

ou com redis-cli:

```
redis-cli -p 6379
```

Sequência esperada:

```
MiniRedis TCP ready
SET nome alice
OK
GET nome
alice
```

---

## O que NÃO fazer

- Lógica de comando dentro de `TcpRedisServer`
- Um cache por cliente (cache deve ser singleton)
- Deixar exceção de IO derrubar o servidor
- `Thread.Sleep` para esperar conexões
- Misturar leitura de rede com lógica de negócio

---

## Classes reutilizadas (sem alteração)

- `CommandParser` — mesma da Parte 3
- `CommandFactory` — mesma da Parte 3
- `ICommand` e implementações — mesmas da Parte 3
- `ICacheStore` / `HashCacheStore` — mesmas da Parte 2
- `SnapshotService` / `CacheRecoveryService` — mesmas da Parte 5

---

## Checklist

- [x] Servidor escuta porta 6379
- [x] Aceita múltiplos clientes (um `Task` por conexão)
- [x] Cada cliente tem seu próprio loop de leitura
- [x] Comandos funcionam via telnet / redis-cli
- [x] Erro em um cliente não derruba o servidor
- [x] Desconexão tratada (`null` no `ReadLineAsync`)
- [x] Cache compartilhado entre clientes
- [x] Shutdown limpo via `CancellationToken`
