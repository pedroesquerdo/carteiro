# Carteiro

Projeto de estudo progressivo sobre envio de e-mails e mensageria.

## Etapa 1 — envio simples via SMTP ✅

Na primeira etapa, o Carteiro era uma aplicação console que enviava um único e-mail via SMTP.

Fluxo:

```text
Carteiro (.NET Console)
        |
        | SMTP
        v
Servidor de e-mail
        |
        v
Destinatário
```

Aprendemos a diferença entre:

- cliente SMTP
- servidor SMTP
- remetente
- destinatário
- autenticação
- TLS/STARTTLS

## Etapa 2 — API HTTP com envio síncrono

Agora o Carteiro é uma API ASP.NET Core.

Fluxo atual:

```text
Cliente HTTP
    |
    | POST /emails
    v
Carteiro API
    |
    | SMTP
    v
Servidor de e-mail
    |
    v
Destinatário
```

Ainda não há banco, fila ou worker. A API espera o SMTP terminar o envio antes de responder à requisição.

Essa limitação é proposital: ela será usada depois para entender por que processamento assíncrono e mensageria são úteis.

## Requisitos

- .NET 8 SDK
- Uma conta/provedor que permita envio via SMTP

## Configuração SMTP

As credenciais ficam em variáveis de ambiente:

- `CARTEIRO_SMTP_HOST`
- `CARTEIRO_SMTP_PORT`
- `CARTEIRO_SMTP_USER`
- `CARTEIRO_SMTP_PASSWORD`

Exemplo com Gmail no PowerShell:

```powershell
$env:CARTEIRO_SMTP_HOST="smtp.gmail.com"
$env:CARTEIRO_SMTP_PORT="587"
$env:CARTEIRO_SMTP_USER="seuemail@gmail.com"
$env:CARTEIRO_SMTP_PASSWORD="SENHA_DE_APP"
```

> Nunca coloque sua senha ou senha de app no GitHub.

## Executar

```powershell
cd D:\Pedro\Projects\carteiro
git pull
dotnet restore
dotnet run
```

O terminal mostrará o endereço em que a API está ouvindo, por exemplo:

```text
Now listening on: http://localhost:5000
```

A porta pode variar. Use o endereço mostrado pelo `dotnet run`.

## Testar a API

No PowerShell, ajuste a URL para a porta exibida no terminal:

```powershell
$body = @{
    to = "destinatario@gmail.com"
    subject = "Teste da API do Carteiro"
    body = "Agora o e-mail foi solicitado por HTTP."
} | ConvertTo-Json

Invoke-RestMethod `
    -Uri "http://localhost:5000/emails" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Requisição HTTP equivalente:

```http
POST /emails
Content-Type: application/json

{
  "to": "destinatario@gmail.com",
  "subject": "Teste da API do Carteiro",
  "body": "Agora o e-mail foi solicitado por HTTP."
}
```

Resposta esperada:

```json
{
  "status": "sent",
  "to": "destinatario@gmail.com",
  "message": "E-mail entregue ao servidor SMTP com sucesso."
}
```

## O que mudou conceitualmente?

Antes:

```text
executar programa -> enviar e-mail -> encerrar
```

Agora:

```text
API permanece ligada
       |
       +-- recebe requisição 1 -> envia e-mail
       +-- recebe requisição 2 -> envia e-mail
       +-- recebe requisição 3 -> envia e-mail
```

Mas o processamento continua síncrono do ponto de vista da requisição:

```text
POST /emails
     |
     v
conectar SMTP
     |
autenticar
     |
enviar
     |
desconectar
     |
responder HTTP
```

Se o servidor SMTP demorar, a requisição HTTP também demora.

## Próximas etapas

1. Envio simples via SMTP ✅
2. Transformar o envio em uma API HTTP ✅
3. Persistir e-mails e status
4. Introduzir processamento assíncrono
5. RabbitMQ: producer, queue e consumer
6. ACK/NACK e retry
7. Dead Letter Queue
8. Idempotência
9. Templates e anexos
10. Observabilidade e padrões de produção
