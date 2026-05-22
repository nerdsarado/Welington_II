using System.Collections.Concurrent;
using System.Text.Json;
using Welington_II.Models;
using System.Runtime.InteropServices;

namespace Welington_II.Services
{
    public class DatabaseJson
    {
        private readonly string _caminhoArquivo;
        private readonly ConcurrentBag<LicitacaoProcessada> _licitacoes;
        private readonly object _lockObject = new object();
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public DatabaseJson()
        {
            _caminhoArquivo = DatabaseConfig.ObterCaminhoBancoDados();
            _licitacoes = new ConcurrentBag<LicitacaoProcessada>();

            // Garante que o diretório existe
            string directory = Path.GetDirectoryName(_caminhoArquivo);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Carregar();

            // Exibe informações de debug
            DatabaseConfig.ExibirInformacoes();
        }

        private void Carregar()
        {
            try
            {
                if (File.Exists(_caminhoArquivo))
                {
                    lock (_lockObject)
                    {
                        string json = File.ReadAllText(_caminhoArquivo);
                        var lista = JsonSerializer.Deserialize<List<LicitacaoProcessada>>(json) ?? new List<LicitacaoProcessada>();
                        foreach (var item in lista)
                        {
                            _licitacoes.Add(item);
                        }
                    }
                    Console.WriteLine($"📂 Carregadas {_licitacoes.Count} licitações processadas");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao carregar banco: {ex.Message}");
            }
        }

        private void Salvar()
        {
            try
            {
                _semaphore.Wait();
                try
                {
                    lock (_lockObject)
                    {
                        var options = new JsonSerializerOptions { WriteIndented = true };
                        string json = JsonSerializer.Serialize(_licitacoes.ToList(), options);
                        string tempFile = _caminhoArquivo + ".tmp";
                        File.WriteAllText(tempFile, json);
                        File.Move(tempFile, _caminhoArquivo, true);
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao salvar banco: {ex.Message}");
            }
        }

        public bool JaFoiProcessada(string idContratacao)
        {
            return _licitacoes.Any(l => l.Id == idContratacao);
        }

        public void AdicionarLicitacao(LicitacaoProcessada licitacao)
        {
            if (!JaFoiProcessada(licitacao.Id))
            {
                _licitacoes.Add(licitacao);
                Salvar();
            }
        }

        public void AdicionarLicitacao(string id, string titulo, string url, string caminhoArquivo = "",
                                        string estado = "", string palavraChave = "", bool aprovado = false,
                                        string orgao = "", string local = "", string valor = "",
                                        string objeto = "", string telefone = "")
        {
            if (!JaFoiProcessada(id))
            {
                var licitacao = new LicitacaoProcessada
                {
                    Id = id,
                    Titulo = titulo,
                    DataProcessamento = DateTime.Now,
                    Url = url,
                    CaminhoArquivo = caminhoArquivo,
                    Estado = estado,
                    PalavraChave = palavraChave,
                    Aprovado = aprovado,
                    Orgao = orgao,
                    Local = local,
                    Valor = valor,
                    Objeto = objeto,
                    Telefone = telefone
                };
                AdicionarLicitacao(licitacao);
            }
        }

        public void ExibirEstatisticas()
        {
            var lista = _licitacoes.ToList();
            Console.WriteLine("\n📊 ESTATÍSTICAS DO BANCO DE DADOS:");
            Console.WriteLine($"Total de licitações processadas: {lista.Count}");

            if (lista.Count > 0)
            {
                var porEstado = lista.GroupBy(l => l.Estado);
                foreach (var grupo in porEstado)
                {
                    Console.WriteLine($"  - {grupo.Key}: {grupo.Count()} licitações");
                }
                Console.WriteLine($"Última atualização: {lista.Max(l => l.DataProcessamento):dd/MM/yyyy HH:mm:ss}");
            }
        }

        public void Limpar()
        {
            _licitacoes.Clear();
            Salvar();
            Console.WriteLine("🗑️ Banco de dados limpo");
        }


    }
    public static class DatabaseConfig
    {
        // Obtém o caminho base para salvar os dados (funciona em Windows e Linux)
        public static string ObterCaminhoBase()
        {
            string homePath;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: C:\Users\Usuario\Documents\WelingtonII
                homePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "WelingtonII"
                );
            }
            else
            {
                // Linux: /home/usuario/.local/share/WelingtonII
                homePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local", "share", "WelingtonII"
                );

                // Alternativa mais simples para Linux: /home/usuario/WelingtonII
                // homePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "WelingtonII");
            }

            // Cria o diretório se não existir
            if (!Directory.Exists(homePath))
            {
                Directory.CreateDirectory(homePath);
            }

            return homePath;
        }

        // Caminho completo do arquivo do banco de dados JSON
        public static string ObterCaminhoBancoDados()
        {
            return Path.Combine(ObterCaminhoBase(), "licitacoes_processadas.json");
        }

        // Caminho da pasta de editais baixados
        public static string ObterCaminhoEditais()
        {
            string editaisPath = Path.Combine(ObterCaminhoBase(), "Editais");

            if (!Directory.Exists(editaisPath))
            {
                Directory.CreateDirectory(editaisPath);
            }

            return editaisPath;
        }

        // Caminho da pasta de logs
        public static string ObterCaminhoLogs()
        {
            string logsPath = Path.Combine(ObterCaminhoBase(), "Logs");

            if (!Directory.Exists(logsPath))
            {
                Directory.CreateDirectory(logsPath);
            }

            return logsPath;
        }

        // Exibe informações de depuração
        public static void ExibirInformacoes()
        {
            Console.WriteLine("=== CONFIGURAÇÃO CROSS-PLATFORM ===");
            Console.WriteLine($"OS: {(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : "Linux")}");
            Console.WriteLine($"📁 Pasta base: {ObterCaminhoBase()}");
            Console.WriteLine($"💾 Banco de dados: {ObterCaminhoBancoDados()}");
            Console.WriteLine($"📄 Editais: {ObterCaminhoEditais()}");
            Console.WriteLine($"📋 Logs: {ObterCaminhoLogs()}");
            Console.WriteLine("====================================");
        }
    }
}