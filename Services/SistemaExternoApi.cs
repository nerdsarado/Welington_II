using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Welington_II.Models;

namespace Welington_II.Services
{
    public class SistemaExternoApi
    {
        private static SistemaExternoApi _instancia;
        private static readonly object _lockInstance = new object();

        private readonly HttpClient _httpClient;
        private readonly string _urlEnvio = "http://localhost:3000/novo-edital";
        private readonly Dictionary<string, TaskCompletionSource<RespostaEdital>> _respostasPendentes;
        private readonly object _lockObject = new object();
        private HttpListener _listener;
        private bool _servidorIniciado = false;
        private static readonly SemaphoreSlim _semaphoreServidor = new SemaphoreSlim(1, 1);

        // Singleton - garante apenas uma instância
        public static SistemaExternoApi Instance
        {
            get
            {
                if (_instancia == null)
                {
                    lock (_lockInstance)
                    {
                        if (_instancia == null)
                        {
                            _instancia = new SistemaExternoApi();
                        }
                    }
                }
                return _instancia;
            }
        }

        private SistemaExternoApi()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _respostasPendentes = new Dictionary<string, TaskCompletionSource<RespostaEdital>>();
            IniciarServidorResposta();
        }

        private void IniciarServidorResposta()
        {
            try
            {
                // Verifica se a porta 8080 já está em uso
                if (IsPortInUse(8080))
                {
                    Console.WriteLine("  ⚠️ Porta 8080 já está em uso. Tentando porta 8081...");

                    // Tenta porta alternativa
                    for (int port = 8081; port <= 8090; port++)
                    {
                        if (!IsPortInUse(port))
                        {
                            _listener = new HttpListener();
                            _listener.Prefixes.Add($"http://localhost:{port}/");
                            _listener.Start();
                            Console.WriteLine($"  ✅ Servidor de resposta iniciado na porta {port}");

                            // Atualiza a URL de resposta que o Node.js deve usar
                            // (Você precisará configurar o Node.js para enviar para esta porta)
                            break;
                        }
                    }

                    if (_listener == null || !_listener.IsListening)
                    {
                        Console.WriteLine("  ❌ Não foi possível iniciar servidor em nenhuma porta de 8080-8090");
                        return;
                    }
                }
                else
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add("http://localhost:8080/");
                    _listener.Start();
                    Console.WriteLine("  ✅ Servidor de resposta iniciado na porta 8080");
                }

                _servidorIniciado = true;
                _ = Task.Run(ProcessarRequisicoes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Erro ao iniciar servidor: {ex.Message}");
            }
        }

        private bool IsPortInUse(int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect("localhost", port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));
                    if (success)
                    {
                        client.EndConnect(result);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private async Task ProcessarRequisicoes()
        {
            while (_listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    var request = context.Request;
                    var response = context.Response;

                    // Aceita qualquer POST
                    if (request.HttpMethod == "POST")
                    {
                        using var reader = new StreamReader(request.InputStream);
                        var body = await reader.ReadToEndAsync();

                        Console.WriteLine($"📨 Resposta recebida: {body}");

                        try
                        {
                            var resposta = JsonSerializer.Deserialize<RespostaEdital>(body);

                            if (resposta != null && !string.IsNullOrEmpty(resposta.id_edital))
                            {
                                Console.WriteLine($"  ✅ ID: {resposta.id_edital}, Aprovado: {resposta.aprovado}");

                                lock (_lockObject)
                                {
                                    if (_respostasPendentes.ContainsKey(resposta.id_edital))
                                    {
                                        _respostasPendentes[resposta.id_edital].SetResult(resposta);
                                        _respostasPendentes.Remove(resposta.id_edital);
                                    }
                                    else
                                    {
                                        Console.WriteLine($"  ⚠️ Nenhum waiter pendente para ID: {resposta.id_edital}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  ❌ Erro ao processar resposta: {ex.Message}");
                        }

                        response.StatusCode = 200;
                        response.Close();
                    }
                    else
                    {
                        response.StatusCode = 405;
                        response.Close();
                    }
                }
                catch (Exception ex)
                {
                    if (_listener != null && _listener.IsListening)
                    {
                        Console.WriteLine($"  ⚠️ Erro no servidor: {ex.Message}");
                    }
                }
            }
        }

        public async Task<(bool aprovado, string telefone)> EnviarEAguardarResposta(
            string idEdital, string orgao, string local, string valor, string objeto)
        {
            try
            {
                var dadosEnvio = new DadosEnvioEdital
                {
                    id_edital = idEdital,
                    orgao = orgao,
                    local = local,
                    valor = valor,
                    objeto = objeto
                };

                var json = JsonSerializer.Serialize(dadosEnvio);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Console.WriteLine($"  📤 Enviando: {idEdital}");

                // Prepara para aguardar resposta
                var tcs = new TaskCompletionSource<RespostaEdital>();
                lock (_lockObject)
                {
                    // Remove pendente anterior se existir
                    if (_respostasPendentes.ContainsKey(idEdital))
                    {
                        _respostasPendentes.Remove(idEdital);
                    }
                    _respostasPendentes[idEdital] = tcs;
                }

                // Envia a requisição
                HttpResponseMessage response = null;
                int tentativas = 0;
                while (tentativas < 3 && response == null)
                {
                    try
                    {
                        response = await _httpClient.PostAsync(_urlEnvio, content);
                    }
                    catch (HttpRequestException ex)
                    {
                        tentativas++;
                        Console.WriteLine($"  ⚠️ Tentativa {tentativas}/3 falhou: {ex.Message}");
                        if (tentativas < 3)
                        {
                            await Task.Delay(5000);
                        }
                    }
                }

                if (response == null || !response.IsSuccessStatusCode)
                {
                    lock (_lockObject)
                    {
                        _respostasPendentes.Remove(idEdital);
                    }
                    Console.WriteLine($"  ⚠️ Erro ao enviar {idEdital}");
                    return (false, "");
                }

                Console.WriteLine($"  ✅ Aguardando resposta...");

                // Aguarda resposta com timeout de 30 minutos
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(30));
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Console.WriteLine($"  ⏰ Timeout: {idEdital}");
                    lock (_lockObject)
                    {
                        _respostasPendentes.Remove(idEdital);
                    }
                    return (false, "");
                }

                var resposta = await tcs.Task;
                Console.WriteLine($"  ✅ Resposta: Aprovado={resposta.aprovado}");
                return (resposta.aprovado, resposta.telefone ?? "");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Erro: {ex.Message}");
                lock (_lockObject)
                {
                    _respostasPendentes.Remove(idEdital);
                }
                return (false, "");
            }
        }

        public static async Task<bool> VerificarApiDisponivel(int timeoutSegundos = 60)
        {
            Console.WriteLine($"\n🔍 Verificando API em http://localhost:3000...");

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalSeconds < timeoutSegundos)
            {
                try
                {
                    var response = await httpClient.GetAsync("http://localhost:3000/health");
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("✅ API disponível!");
                        return true;
                    }
                }
                catch { }

                await Task.Delay(2000);
                Console.Write(".");
            }

            Console.WriteLine($"\n❌ API não disponível");
            return false;
        }

        public static async Task AguardarApiDisponivel()
        {
            Console.WriteLine("\n⚠️ Aguardando sistema externo (Node.js)...");
            Console.WriteLine("   Certifique-se que o servidor está rodando em http://localhost:3000");

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(2);

            while (true)
            {
                try
                {
                    var response = await httpClient.GetAsync("http://localhost:3000/health");
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("\n✅ API conectada!");
                        return;
                    }
                }
                catch { }

                Console.Write(".");
                await Task.Delay(2000);
            }
        }
    }
}