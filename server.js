const express = require('express');
const app = express();

app.use(express.json());

// Endpoint que o C# chama
app.post('/novo-edital', (req, res) => {
    console.log('📥 Recebido do C#:', req.body);

    // Simula processamento
    const aprovado = Math.random() > 0.5; // 50% de chance

    // Envia resposta para a porta 5000 (onde o C# está ouvindo)
    setTimeout(() => {
        const resposta = {
            telefone: "5511999999999@c.us",
            id_edital: req.body.id_edital,
            aprovado: aprovado
        };

        // Envia para o C#
        fetch('http://localhost:8080/resposta-edital', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(resposta)
        }).then(() => {
            console.log('📤 Resposta enviada para C#:', resposta);
        }).catch(err => {
            console.error('❌ Erro ao enviar resposta:', err);
        });
    }, 2000);

    res.json({ status: 'recebido' });
});

// Health check
app.get('/health', (req, res) => {
    res.json({ status: 'ok' });
});

app.listen(3000, () => {
    console.log('🚀 Servidor Node.js rodando na porta 3000');
    console.log('   Aguardando requisições do C#...');
});