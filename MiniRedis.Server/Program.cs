using System;
using MiniRedis.Core.Cache;

class Program
{
    static void Main()
    {
        var cache = new HashCacheStore();
        Console.WriteLine("MiniRedis Server started!");

        while (true)
        {
            Console.WriteLine("\nEscolha uma opção:");
            Console.WriteLine("1 - Adicionar");
            Console.WriteLine("2 - Remover");
            Console.WriteLine("3 - Buscar");
            Console.WriteLine("4 - Listar chaves");
            Console.WriteLine("5 - Limpar cache");
            Console.WriteLine("0 - Sair");

            Console.Write("Opção: ");
            var opc = Console.ReadLine();

            switch (opc)
            {
                case "1":
                    Console.Write("Chave: ");
                    var chaveAdd = Console.ReadLine();
                    Console.Write("Valor: ");
                    var valorAdd = Console.ReadLine();
                    Console.Write("TTL em segundos: ");
                    if (int.TryParse(Console.ReadLine(), out int ttlSegundos))
                    {
                        cache.Set(chaveAdd!, valorAdd!, TimeSpan.FromSeconds(ttlSegundos));
                        Console.WriteLine("Adicionado!");
                    }
                    else
                    {
                        Console.WriteLine("TTL inválido!");
                    }
                    break;

                case "2":
                    Console.Write("Chave a remover: ");
                    var chaveDel = Console.ReadLine();
                    cache.Delete(chaveDel!);
                    Console.WriteLine("Removido se existia.");
                    break;

                case "3":
                    Console.Write("Chave a buscar: ");
                    var chaveGet = Console.ReadLine();
                    var valor = cache.Get(chaveGet!);
                    Console.WriteLine(valor ?? "Não existe ou expirou.");
                    break;

                case "4":
                    Console.WriteLine("Chaves no cache:");
                    foreach (var key in cache.GetAllKeys())
                    {
                        Console.WriteLine(key);
                    }
                    break;

                case "5":
                    cache.Clear();
                    Console.WriteLine("Cache limpo!");
                    break;

                case "0":
                    return;

                default:
                    Console.WriteLine("Opção inválida!");
                    break;
            }
        }
    }
}
