using System.Collections.Concurrent;
using Microsoft.Playwright;
using Welington_II.Models;

namespace Welington_II.Services
{
    public class ProcessadorComFila
    {
        private readonly SistemaExternoApi _api;  // Remover instância própria
        private readonly DatabaseJson _db;
        private readonly int _maxConcorrencia;
        private int _totalProcessadas;
        private int _totalAprovadas;
        private int _totalReprovadas;
        private readonly object _lockObject = new object();

        public ProcessadorComFila(DatabaseJson db, int maxConcorrencia = 5)
        {
            _api = SistemaExternoApi.Instance;  // Usa Singleton
            _db = db;
            _maxConcorrencia = maxConcorrencia;
            _totalProcessadas = 0;
            _totalAprovadas = 0;
            _totalReprovadas = 0;
        }

        public async Task ProcessarLote(List<LicitacaoInfo> licitacoes, IBrowser browser, string estado, string palavraChave)
        {
            if (!licitacoes.Any())
            {
                Console.WriteLine("📭 Nenhuma licitação para processar neste lote.");
                return;
            }

            Console.WriteLine($"\n🚀 Iniciando processamento de lote com {licitacoes.Count} licitações");
            Console.WriteLine($"👷 Workers simultâneos: {_maxConcorrencia}");

            // Usa uma fila local para este lote
            var fila = new ConcurrentQueue<LicitacaoInfo>();
            foreach (var lic in licitacoes)
            {
                fila.Enqueue(lic);
            }

            // Reseta contadores
            lock (_lockObject)
            {
                _totalProcessadas = 0;
                _totalAprovadas = 0;
                _totalReprovadas = 0;
            }

            // Cria e inicia os workers
            var workers = new List<Task>();
            for (int i = 0; i < Math.Min(_maxConcorrencia, licitacoes.Count); i++)
            {
                int workerId = i + 1;
                workers.Add(Task.Run(() => Worker(workerId, browser, fila, estado, palavraChave)));
            }

            // Aguarda todos os workers terminarem
            await Task.WhenAll(workers);

            Console.WriteLine($"\n📊 RESUMO DO LOTE:");
            Console.WriteLine($"   Total processadas: {_totalProcessadas}");
            Console.WriteLine($"   Aprovadas: {_totalAprovadas}");
            Console.WriteLine($"   Reprovadas: {_totalReprovadas}");
        }

        private async Task Worker(int workerId, IBrowser browser, ConcurrentQueue<LicitacaoInfo> fila, string estado, string palavraChave)
        {
            Console.WriteLine($"👷 Worker {workerId} iniciado");

            while (fila.TryDequeue(out var licitacao))
            {
                Console.WriteLine($"\n👷 Worker {workerId} pegou: {licitacao.Titulo}");

                try
                {
                    var contexto = await browser.NewContextAsync(new BrowserNewContextOptions
                    {
                        IgnoreHTTPSErrors = true
                    });

                    try
                    {
                        await ProcessarLicitacaoComAprovacao(contexto, licitacao, estado, palavraChave, workerId);
                    }
                    finally
                    {
                        await contexto.CloseAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Worker {workerId} - Erro em {licitacao.Titulo}: {ex.Message}");
                }
            }

            Console.WriteLine($"👷 Worker {workerId} finalizado - não há mais licitações na fila");
        }

        private async Task ProcessarLicitacaoComAprovacao(IBrowserContext contexto, LicitacaoInfo licitacao,
                                                           string estado, string palavraChave, int workerId)
        {
            IPage pagina = null;
            bool aprovado = false;
            string telefone = "";

            try
            {
                Console.WriteLine($"  🎯 Worker {workerId} - Iniciando: {licitacao.Titulo}");

                pagina = await contexto.NewPageAsync();
                pagina.SetDefaultTimeout(60000);

                var response = await pagina.GotoAsync(licitacao.Url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 45000
                });

                if (response?.Status != 200)
                {
                    Console.WriteLine($"  ⚠️ Worker {workerId} - Falha ao carregar {licitacao.Titulo}: Status {response?.Status}");
                    return;
                }

                await Task.Delay(5000);

                // Extrai informações da página
                var (valorExtraido, objetoExtraido, orgaoExtraido, localExtraido) = await Automacao.ExtrairInformacoesPagina(pagina);

                string valorFinal = string.IsNullOrEmpty(valorExtraido) ? licitacao.Valor : valorExtraido;
                string objetoFinal = string.IsNullOrEmpty(objetoExtraido) ? licitacao.Objeto : objetoExtraido;
                string orgaoFinal = string.IsNullOrEmpty(orgaoExtraido) ? licitacao.Orgao : orgaoExtraido;
                string localFinal = string.IsNullOrEmpty(localExtraido) ? licitacao.Local : localExtraido;

                Console.WriteLine($"  📋 Worker {workerId} - Dados enviados para API:");
                Console.WriteLine($"     ID: {licitacao.Id}");
                Console.WriteLine($"     Órgão: {orgaoFinal}");
                Console.WriteLine($"     Local: {localFinal}");
                Console.WriteLine($"     Valor: {valorFinal}");

                Console.WriteLine($"  ⏳ Worker {workerId} - Aguardando resposta da API...");
                (aprovado, telefone) = await _api.EnviarEAguardarResposta(
                    licitacao.Id, orgaoFinal, localFinal, valorFinal, objetoFinal);

                string caminhoArquivo = "";
                if (aprovado)
                {
                    Console.WriteLine($"  ✅ Worker {workerId} - Licitação APROVADA: {licitacao.Titulo}");
                    caminhoArquivo = await Automacao.ProcessarArquivosLicitaçãoComRetorno(pagina, licitacao.Titulo, licitacao.Id);
                    lock (_lockObject) { _totalAprovadas++; }
                }
                else
                {
                    Console.WriteLine($"  ❌ Worker {workerId} - Licitação REPROVADA: {licitacao.Titulo}");
                    lock (_lockObject) { _totalReprovadas++; }
                }

                _db.AdicionarLicitacao(licitacao.Id, licitacao.Titulo, licitacao.Url, caminhoArquivo,
                    estado, palavraChave, aprovado, orgaoFinal, localFinal, valorFinal, objetoFinal, telefone);

                lock (_lockObject) { _totalProcessadas++; }

                Console.WriteLine($"  💾 Worker {workerId} - Licitação registrada no banco. Total processadas: {_totalProcessadas}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Worker {workerId} - Erro em {licitacao.Titulo}: {ex.Message}");
                Console.WriteLine($"  ⚠️ Licitação NÃO foi salva no banco de dados devido ao erro");
            }
            finally
            {
                if (pagina != null)
                    await pagina.CloseAsync();
            }
        }

        public int TotalProcessadas => _totalProcessadas;
        public int TotalAprovadas => _totalAprovadas;
        public int TotalReprovadas => _totalReprovadas;
    }
}