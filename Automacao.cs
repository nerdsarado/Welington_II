using Microsoft.Playwright;
using System.Text.RegularExpressions;
using Welington_II.Models;
using Welington_II.Services;

namespace Welington_II
{
    public class Automacao
    {
        private static string DiretorioEditais => DatabaseConfig.ObterCaminhoEditais();
        private static DatabaseJson _db = new DatabaseJson();

        public static async Task<IPage> InicializarBrowser()
        {
            string browsersPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Navegadores");
            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", browsersPath);

            // Força a recarga da variável de ambiente (evita cache)
            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", browsersPath, EnvironmentVariableTarget.Process);
            IPlaywright playwright = await Playwright.CreateAsync();
            IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false
            });
            IPage page = await browser.NewPageAsync();
            await page.GotoAsync("https://pncp.gov.br/app/editais?pagina=1");
            return page;
        }

        public static async Task SelecionarEstado(IPage page, string estado)
        {
            try
            {
                await page.WaitForSelectorAsync("ng-select#ufs", new PageWaitForSelectorOptions
                {
                    State = WaitForSelectorState.Attached
                });

                await page.ClickAsync("ng-select#ufs");
                await Task.Delay(500);

                string estadoUpper = estado.ToUpper();
                string selector = $"ng-dropdown-panel .ng-option:has-text('{estadoUpper}')";

                await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 5000
                });

                await page.ClickAsync(selector);
                Console.WriteLine($"Estado {estadoUpper} selecionado com sucesso!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao selecionar estado {estado}: {ex.Message}");
            }
        }

        public static async Task PesquisarPalavraChave(IPage page, string palavra)
        {
            try
            {
                var inputKeyword = page.Locator("input#keyword");
                await inputKeyword.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 5000
                });

                await inputKeyword.ClearAsync();
                await inputKeyword.FillAsync(palavra);
                Console.WriteLine($"Palavra-chave '{palavra}' digitada com sucesso!");
                await inputKeyword.PressAsync("Enter");
                await Task.Delay(2000);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao pesquisar palavra '{palavra}': {ex.Message}");
            }
        }

        public static async Task SelecionarItensPorPagina(IPage page, int quantidade)
        {
            try
            {
                Console.WriteLine($"\n=== Selecionando {quantidade} itens por página ===");
                await page.WaitForSelectorAsync("ng-select#tam_pagina", new PageWaitForSelectorOptions
                {
                    State = WaitForSelectorState.Attached,
                    Timeout = 10000
                });

                await page.ClickAsync("ng-select#tam_pagina");
                await Task.Delay(500);
                await page.WaitForSelectorAsync("ng-dropdown-panel .ng-option", new PageWaitForSelectorOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 5000
                });

                string selectorOpcao = $"ng-dropdown-panel .ng-option:has-text('{quantidade}')";
                var opcao = page.Locator(selectorOpcao);
                bool existe = await opcao.IsVisibleAsync();

                if (existe)
                {
                    await opcao.ClickAsync();
                    Console.WriteLine($"✅ Selecionado {quantidade} itens por página");
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    await Task.Delay(2000);
                }
                else
                {
                    Console.WriteLine($"❌ Opção {quantidade} não encontrada");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao selecionar itens por página: {ex.Message}");
            }
        }

        public static async Task<List<LicitacaoInfo>> ColetarLicitacoesPagina(IPage page, DatabaseJson db)
        {
            var resultado = new List<LicitacaoInfo>();
            var idsProcessados = new HashSet<string>();

            try
            {
                await page.WaitForSelectorAsync("a.br-item", new PageWaitForSelectorOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 15000
                });

                int totalElementos = await page.Locator("a.br-item").CountAsync();
                Console.WriteLine($"Total de elementos encontrados na página: {totalElementos}");

                for (int i = 0; i < totalElementos; i++)
                {
                    try
                    {
                        var linkLicitacao = page.Locator("a.br-item").Nth(i);
                        string href = await linkLicitacao.GetAttributeAsync("href") ?? "";

                        if (string.IsNullOrEmpty(href)) continue;

                        string titulo = "";
                        try
                        {
                            var tituloElement = linkLicitacao.Locator("strong").First;
                            if (await tituloElement.CountAsync() > 0)
                                titulo = await tituloElement.TextContentAsync() ?? "Sem título";
                        }
                        catch { }

                        string id = "";
                        try
                        {
                            var idElement = linkLicitacao.Locator("span:has-text('Id contratação PNCP:')");
                            if (await idElement.CountAsync() > 0)
                            {
                                string textoId = await idElement.TextContentAsync() ?? "";
                                id = textoId.Replace("Id contratação PNCP:", "").Trim();
                            }
                        }
                        catch { }

                        if (string.IsNullOrEmpty(id)) continue;

                        // 🔥 VERIFICA DUPLICATA NA PRÓPRIA PÁGINA
                        if (idsProcessados.Contains(id))
                        {
                            Console.WriteLine($"  ⏭️ Duplicata ignorada: {titulo} - {id}");
                            continue;
                        }

                        // 🔥 VERIFICA SE JÁ FOI PROCESSADA NO BANCO
                        if (db.JaFoiProcessada(id))
                        {
                            Console.WriteLine($"  ⏭️ Já processada anteriormente: {titulo} - {id}");
                            continue;
                        }

                        idsProcessados.Add(id); // Marca como processado nesta rodada

                        string orgao = "";
                        try
                        {
                            var orgaoElement = linkLicitacao.Locator("span:has-text('Órgão:')");
                            if (await orgaoElement.CountAsync() > 0)
                            {
                                string textoOrgao = await orgaoElement.TextContentAsync() ?? "";
                                orgao = textoOrgao.Replace("Órgão:", "").Trim();
                            }
                        }
                        catch { }

                        string local = "";
                        try
                        {
                            var localElement = linkLicitacao.Locator("span:has-text('Local:')");
                            if (await localElement.CountAsync() > 0)
                            {
                                string textoLocal = await localElement.TextContentAsync() ?? "";
                                local = textoLocal.Replace("Local:", "").Trim();
                            }
                        }
                        catch { }

                        string objeto = "";
                        try
                        {
                            var objetoElement = linkLicitacao.Locator("span:has-text('Objeto:')");
                            if (await objetoElement.CountAsync() > 0)
                            {
                                string textoObjeto = await objetoElement.TextContentAsync() ?? "";
                                objeto = textoObjeto.Replace("Objeto:", "").Trim();
                                if (objeto.Length > 200)
                                    objeto = objeto.Substring(0, 200);
                            }
                        }
                        catch { }

                        string url = href.StartsWith("http") ? href : $"https://pncp.gov.br/app{href}";

                        resultado.Add(new LicitacaoInfo
                        {
                            Url = url,
                            Titulo = titulo,
                            Id = id,
                            Orgao = orgao,
                            Local = local,
                            Valor = "",
                            Objeto = objeto
                        });

                        Console.WriteLine($"  ✅ Coletada: {titulo} - {id}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao coletar item {i}: {ex.Message}");
                    }
                }

                Console.WriteLine($"📊 Licitações únicas coletadas: {resultado.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao coletar licitações: {ex.Message}");
            }

            return resultado;
        }

        public static async Task<bool> IrParaProximaPagina(IPage page)
        {
            try
            {
                Console.WriteLine("\n--- Verificando próxima página ---");

                var botaoProxima = page.Locator("button[aria-label='Página seguinte']");

                if (await botaoProxima.CountAsync() == 0)
                    botaoProxima = page.Locator("button[data-next-page]");
                if (await botaoProxima.CountAsync() == 0)
                    botaoProxima = page.Locator(".pagination-arrows button:last-child");
                if (await botaoProxima.CountAsync() == 0)
                    botaoProxima = page.Locator("button:has(i.fa-chevron-right), button:has(i.fa-angle-right)");

                if (await botaoProxima.CountAsync() == 0)
                {
                    Console.WriteLine("❌ Botão de próxima página não encontrado");
                    return false;
                }

                bool isDisabled = false;
                try
                {
                    var disabledAttr = await botaoProxima.First.GetAttributeAsync("disabled");
                    isDisabled = disabledAttr != null;

                    if (!isDisabled)
                    {
                        var classAttr = await botaoProxima.First.GetAttributeAsync("class");
                        isDisabled = classAttr != null && classAttr.Contains("disabled");
                    }
                }
                catch { }

                if (isDisabled)
                {
                    Console.WriteLine("⚠️ Botão de próxima página desabilitado (última página)");
                    return false;
                }

                Console.WriteLine("✅ Clicando no botão de próxima página...");
                await botaoProxima.First.ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(3000);
                await page.WaitForSelectorAsync("a.br-item", new PageWaitForSelectorOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 10000
                });

                Console.WriteLine("✅ Próxima página carregada com sucesso!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao navegar para próxima página: {ex.Message}");
                return false;
            }
        }
        // Método para extrair informações específicas da página
        public static async Task<(string valor, string objeto, string orgao, string local)> ExtrairInformacoesPagina(IPage page)
        {
            string valor = "";
            string objeto = "";
            string orgao = "";
            string local = "";

            try
            {
                // Extrai o VALOR - procurando especificamente "VALOR TOTAL ESTIMADO"
                try
                {
                    var valorElement = page.Locator("text=/VALOR TOTAL ESTIMADO.*?R\\$\\s*\\d{1,3}(?:\\.\\d{3})*(?:,\\d{2})/i");
                    if (await valorElement.CountAsync() > 0)
                    {
                        var texto = await valorElement.First.TextContentAsync() ?? "";
                        var match = Regex.Match(texto, @"R\$\s*\d{1,3}(?:\.\d{3})*(?:,\d{2})");
                        if (match.Success)
                            valor = match.Value;
                    }
                }
                catch { }

                // Se não achou, tenta o seletor alternativo
                if (string.IsNullOrEmpty(valor))
                {
                    try
                    {
                        var valorElement = page.Locator("text=/R\\$\\s*\\d{1,3}(?:\\.\\d{3})*(?:,\\d{2})/").First;
                        valor = await valorElement.TextContentAsync() ?? "";
                    }
                    catch { }
                }

                // Extrai o OBJETO - especificamente do span.conteudo-objeto
                try
                {
                    var objetoElement = page.Locator("span.conteudo-objeto");
                    if (await objetoElement.CountAsync() > 0)
                    {
                        objeto = await objetoElement.First.TextContentAsync() ?? "";
                        objeto = objeto.Trim();
                    }
                }
                catch { }

                // Extrai o ÓRGÃO - especificamente o campo correto
                try
                {
                    // Procura pelo texto "Órgão:" e pega o próximo elemento
                    var orgaoElement = page.Locator("text=/Órgão:\\s*([^\\n]+)/i");
                    if (await orgaoElement.CountAsync() > 0)
                    {
                        var texto = await orgaoElement.First.TextContentAsync() ?? "";
                        var match = Regex.Match(texto, @"Órgão:\s*(.+)", RegexOptions.IgnoreCase);
                        if (match.Success)
                            orgao = match.Groups[1].Value.Trim();
                    }
                }
                catch { }

                // Se não achou, tenta o seletor alternativo
                if (string.IsNullOrEmpty(orgao))
                {
                    try
                    {
                        var elementos = page.Locator("div:has-text('Órgão:'), p:has-text('Órgão:'), span:has-text('Órgão:')");
                        for (int i = 0; i < await elementos.CountAsync(); i++)
                        {
                            var texto = await elementos.Nth(i).TextContentAsync() ?? "";
                            if (texto.Contains("Órgão:") && texto.Length < 200)
                            {
                                var match = Regex.Match(texto, @"Órgão:\s*(.+)", RegexOptions.IgnoreCase);
                                if (match.Success)
                                {
                                    orgao = match.Groups[1].Value.Trim();
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Extrai o LOCAL - especificamente o campo correto
                try
                {
                    var localElement = page.Locator("text=/Local:\\s*([^\\n]+)/i");
                    if (await localElement.CountAsync() > 0)
                    {
                        var texto = await localElement.First.TextContentAsync() ?? "";
                        var match = Regex.Match(texto, @"Local:\s*(.+)", RegexOptions.IgnoreCase);
                        if (match.Success)
                            local = match.Groups[1].Value.Trim();
                    }
                }
                catch { }

                // Limpa os valores
                valor = valor.Trim();
                objeto = objeto.Trim();
                orgao = orgao.Trim();
                local = local.Trim();

                // Validação básica
                if (!string.IsNullOrEmpty(valor))
                    Console.WriteLine($"  💰 Valor: {valor}");
                if (!string.IsNullOrEmpty(objeto))
                    Console.WriteLine($"  📝 Objeto: {(objeto.Length > 80 ? objeto.Substring(0, 80) + "..." : objeto)}");
                if (!string.IsNullOrEmpty(orgao))
                    Console.WriteLine($"  🏢 Órgão: {orgao}");
                if (!string.IsNullOrEmpty(local))
                    Console.WriteLine($"  📍 Local: {local}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Erro ao extrair informações: {ex.Message}");
            }

            return (valor, objeto, orgao, local);
        }
        public static async Task<string> ProcessarArquivosLicitaçãoComRetorno(IPage page, string tituloEdital, string idContratacao)
        {
            string caminhoArquivoSalvo = "";

            try
            {
                Console.WriteLine($"\n--- Verificando arquivos da licitação: {tituloEdital} ---");

                if (!Directory.Exists(DiretorioEditais))
                {
                    Directory.CreateDirectory(DiretorioEditais);
                    Console.WriteLine($"📁 Diretório criado: {DiretorioEditais}");
                }

                var botaoArquivos = page.Locator("button:has-text('Arquivos')");

                if (await botaoArquivos.CountAsync() == 0)
                {
                    Console.WriteLine($"⚠️ Botão 'Arquivos' não encontrado para {tituloEdital}");
                    return "";
                }

                await botaoArquivos.First.ClickAsync();
                await Task.Delay(3000);

                var linhasTabela = page.Locator("datatable-body-row");
                int quantidadeLinhas = await linhasTabela.CountAsync();

                if (quantidadeLinhas == 0)
                {
                    await Task.Delay(2000);
                    quantidadeLinhas = await linhasTabela.CountAsync();
                }

                Console.WriteLine($"📋 Encontradas {quantidadeLinhas} linhas na tabela de arquivos");

                for (int i = 0; i < quantidadeLinhas; i++)
                {
                    try
                    {
                        var linha = linhasTabela.Nth(i);
                        await Task.Delay(300);

                        string tipoArquivo = "";

                        try
                        {
                            var spanTipo = linha.Locator("datatable-body-cell:nth-child(3) span[title]");
                            if (await spanTipo.CountAsync() > 0)
                            {
                                tipoArquivo = await spanTipo.First.GetAttributeAsync("title") ?? "";
                            }
                        }
                        catch { }

                        if (string.IsNullOrEmpty(tipoArquivo))
                        {
                            try
                            {
                                var celulaTipo = linha.Locator("datatable-body-cell:nth-child(3)");
                                tipoArquivo = await celulaTipo.TextContentAsync() ?? "";
                                tipoArquivo = tipoArquivo.Trim();
                            }
                            catch { }
                        }

                        bool isEdital = !string.IsNullOrEmpty(tipoArquivo) &&
                                        tipoArquivo.Contains("Edital", StringComparison.OrdinalIgnoreCase);

                        if (isEdital)
                        {
                            Console.WriteLine($"📄 EDITAL encontrado! Baixando...");

                            string nomeArquivoOriginal = "";
                            try
                            {
                                var spanNome = linha.Locator("datatable-body-cell:first-child span[title]");
                                if (await spanNome.CountAsync() > 0)
                                {
                                    nomeArquivoOriginal = await spanNome.First.GetAttributeAsync("title") ?? "";
                                }
                            }
                            catch { }

                            string urlDownload = "";
                            try
                            {
                                var linkDownload = linha.Locator("a.br-button[aria-label='Fazer download']");
                                if (await linkDownload.CountAsync() > 0)
                                {
                                    urlDownload = await linkDownload.First.GetAttributeAsync("href") ?? "";
                                }
                            }
                            catch { }

                            if (!string.IsNullOrEmpty(urlDownload))
                            {
                                string urlCompleta = urlDownload.StartsWith("http")
                                    ? urlDownload
                                    : $"https://pncp.gov.br{urlDownload}";

                                string nomeLimpo = Regex.Replace(tituloEdital, @"[^\w\s]", "_");
                                nomeLimpo = Regex.Replace(nomeLimpo, @"\s+", "_");
                                if (nomeLimpo.Length > 50) nomeLimpo = nomeLimpo.Substring(0, 50);

                                string idLimpo = idContratacao.Replace("/", "-").Replace("\\", "-");
                                string dataAtual = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                                string extensao = ".pdf";
                                if (!string.IsNullOrEmpty(nomeArquivoOriginal))
                                {
                                    string extensaoOriginal = Path.GetExtension(nomeArquivoOriginal);
                                    if (!string.IsNullOrEmpty(extensaoOriginal)) extensao = extensaoOriginal;
                                }

                                string nomeArquivoSalvar = $"EDITAL_{nomeLimpo}_{idLimpo}_{dataAtual}{extensao}";
                                caminhoArquivoSalvo = Path.Combine(DiretorioEditais, nomeArquivoSalvar);

                                using (var httpClient = new HttpClient())
                                {
                                    httpClient.Timeout = TimeSpan.FromMinutes(5);
                                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
                                    var response = await httpClient.GetAsync(urlCompleta);
                                    if (response.IsSuccessStatusCode)
                                    {
                                        byte[] content = await response.Content.ReadAsByteArrayAsync();
                                        await File.WriteAllBytesAsync(caminhoArquivoSalvo, content);
                                        Console.WriteLine($"✅ Arquivo salvo: {caminhoArquivoSalvo}");
                                    }
                                }
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao processar linha {i + 1}: {ex.Message}");
                    }
                }

                await botaoArquivos.First.ClickAsync();
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao processar arquivos: {ex.Message}");
            }

            return caminhoArquivoSalvo;
        }
    }
}