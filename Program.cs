using Microsoft.Playwright;
using Welington_II;
using Welington_II.Services;

class Program
{
    private static bool _executando = true;
    private static DateTime _proximaExecucao;
    private static int _cicloAtual = 0;

    static async Task Main(string[] args)
    {
        // Configura intervalo padrão (4 horas = 240 minutos)
        int intervaloHoras = 4;

        // Permite configurar via argumento: WelingtonII.exe --intervalo 2
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--intervalo" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int horas))
                {
                    intervaloHoras = horas;
                    Console.WriteLine($"⏰ Intervalo configurado para {intervaloHoras} horas");
                }
            }
            if (args[i] == "--help")
            {
                Console.WriteLine("Uso: WelingtonII.exe [--intervalo N]");
                Console.WriteLine("  --intervalo N: Define intervalo em horas entre execuções (padrão: 4)");
                Console.WriteLine("  --help: Exibe esta ajuda");
                return;
            }
        }

        Console.CancelKeyPress += (sender, e) =>
        {
            Console.WriteLine("\n\n⚠️ Recebido sinal de cancelamento. Finalizando...");
            _executando = false;
            e.Cancel = true;
        };

        while (_executando)
        {
            _cicloAtual++;
            Console.WriteLine($"\n{new string('=', 70)}");
            Console.WriteLine($"🔄 INICIANDO CICLO DE EXECUÇÃO #{_cicloAtual}");
            Console.WriteLine($"📅 {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            Console.WriteLine($"⏰ Intervalo entre ciclos: {intervaloHoras} horas");
            Console.WriteLine($"{new string('=', 70)}\n");

            await ExecutarCiclo();

            if (!_executando) break;

            _proximaExecucao = DateTime.Now.AddHours(intervaloHoras);
            await AguardarProximoCiclo(intervaloHoras);
        }

        Console.WriteLine("\n✅ Sistema finalizado permanentemente.");
        Console.WriteLine("Pressione ENTER para sair...");
        Console.ReadLine();
    }

    static async Task ExecutarCiclo()
    {
        IPage page = null;
        IBrowser browser = null;

        try
        {
            var db = new DatabaseJson();
            db.ExibirEstatisticas();

            Console.WriteLine("\n🔌 Verificando sistema externo...");
            await SistemaExternoApi.AguardarApiDisponivel();

            Console.WriteLine("\n⚙️ Configurações:");
            int maxConcorrencia = 5;
            Console.WriteLine($"   Processos simultâneos: {maxConcorrencia}");
            Console.WriteLine($"   Intervalo entre ciclos: 4 horas");

            var estados = new List<string> { "MS", "ES", "MG", "RS", "SC" };
            var palavras = new List<string> {
                "elaboração de projeto",
                "geotech",
                "topografia",
                "projeto de pavimentação",
                "drenagem"
            };

            // 🔥 CORREÇÃO: Usar o método da classe Automacao
            page = await Automacao.InicializarBrowser();

            // 🔥 CORREÇÃO: Obter o browser a partir da página
            browser = page.Context.Browser;

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Automacao.SelecionarItensPorPagina(page, 100);

            foreach (var estado in estados)
            {
                if (!_executando) break;

                Console.WriteLine($"\n{new string('=', 60)}");
                Console.WriteLine($"📍 PROCESSANDO ESTADO: {estado}");
                Console.WriteLine($"{new string('=', 60)}\n");

                await Automacao.SelecionarEstado(page, estado);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(3000);

                foreach (var palavra in palavras)
                {
                    if (!_executando) break;

                    Console.WriteLine($"\n🔍 Pesquisando: {palavra}");
                    await Automacao.PesquisarPalavraChave(page, palavra);
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    await Task.Delay(2000);

                    bool temProxima = true;
                    int paginaNum = 1;

                    while (temProxima && _executando)
                    {
                        Console.WriteLine($"\n📄 Página {paginaNum} - {palavra}");

                        var licitacoes = await Automacao.ColetarLicitacoesPagina(page, db);

                        if (licitacoes.Any())
                        {
                            Console.WriteLine($"🚀 Processando {licitacoes.Count} licitações...");
                            var processador = new ProcessadorComFila(db, maxConcorrencia);
                            await processador.ProcessarLote(licitacoes, browser, estado, palavra);
                        }
                        else
                        {
                            Console.WriteLine("⚠️ Nenhuma licitação nova nesta página.");
                        }

                        temProxima = await Automacao.IrParaProximaPagina(page);
                        if (temProxima && _executando)
                        {
                            paginaNum++;
                            await Task.Delay(2000);
                        }
                    }
                }
            }

            db.ExibirEstatisticas();
            Console.WriteLine($"\n✅ Ciclo #{_cicloAtual} concluído com sucesso!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Erro no ciclo #{_cicloAtual}: {ex.Message}");
            Console.WriteLine($"StackTrace: {ex.StackTrace}");
        }
        finally
        {
            Console.WriteLine("\n🔒 Finalizando navegadores...");

            if (page != null)
                await page.CloseAsync();

            Console.WriteLine("✅ Recursos liberados.");
        }
    }

    static async Task AguardarProximoCiclo(int intervaloHoras)
    {
        Console.WriteLine($"\n{new string('=', 70)}");
        Console.WriteLine($"⏰ PRÓXIMA EXECUÇÃO: {_proximaExecucao:dd/MM/yyyy HH:mm:ss}");
        Console.WriteLine($"💤 Aguardando {intervaloHoras} horas antes do próximo ciclo...");
        Console.WriteLine($"   Pressione CTRL+C para cancelar e encerrar o programa.");
        Console.WriteLine($"{new string('=', 70)}");

        var tempoRestante = TimeSpan.FromHours(intervaloHoras);

        while (tempoRestante.TotalSeconds > 0 && _executando)
        {
            tempoRestante = _proximaExecucao - DateTime.Now;

            if (tempoRestante.TotalSeconds > 0)
            {
                string tempoFormatado;
                if (tempoRestante.TotalHours >= 1)
                    tempoFormatado = $"{tempoRestante.Hours:D2}h {tempoRestante.Minutes:D2}m {tempoRestante.Seconds:D2}s";
                else if (tempoRestante.TotalMinutes >= 1)
                    tempoFormatado = $"{tempoRestante.Minutes:D2}m {tempoRestante.Seconds:D2}s";
                else
                    tempoFormatado = $"{tempoRestante.Seconds:D2}s";

                Console.Write($"\r⏳ Próximo ciclo em: {tempoFormatado}    ");
                await Task.Delay(1000);
            }
        }

        Console.WriteLine();

        if (_executando)
        {
            Console.WriteLine($"\n✅ Hora de começar o próximo ciclo!");
            await Task.Delay(2000);
        }
    }
}