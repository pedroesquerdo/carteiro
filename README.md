# Carteiro

Projeto de estudo progressivo sobre envio de e-mails e mensageria.

## Etapa 1 — envio simples via SMTP

Objetivo desta etapa: executar o Carteiro pelo terminal e enviar um único e-mail usando um servidor SMTP.

Fluxo atual:

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

Ainda não temos API, banco de dados, fila ou worker. Esses componentes serão adicionados apenas quando surgirem os problemas que eles resolvem.

## Requisitos

- .NET 8 SDK
- Uma conta/provedor que permita envio via SMTP

## Configuração

As credenciais não ficam no código. O programa lê estas variáveis de ambiente:

- `CARTEIRO_SMTP_HOST`
- `CARTEIRO_SMTP_PORT`
- `CARTEIRO_SMTP_USER`
- `CARTEIRO_SMTP_PASSWORD`
- `CARTEIRO_TO`

### Exemplo com Gmail no PowerShell

```powershell
$env:CARTEIRO_SMTP_HOST="smtp.gmail.com"
$env:CARTEIRO_SMTP_PORT="587"
$env:CARTEIRO_SMTP_USER="seuemail@gmail.com"
$env:CARTEIRO_SMTP_PASSWORD="SENHA_DE_APP"
$env:CARTEIRO_TO="destinatario@gmail.com"
```

> Nunca coloque sua senha ou senha de app no GitHub.

## Executar

Dentro da pasta do projeto:

```powershell
dotnet restore
dotnet run
```

Se funcionar, o terminal deverá mostrar aproximadamente:

```text
Preparando e-mail...
Conectando ao servidor SMTP...
Autenticando no servidor SMTP...
Enviando mensagem...
E-mail entregue ao servidor SMTP com sucesso.
```

## Conceitos desta etapa

Antes de avançar, queremos entender claramente:

- remetente
- destinatário
- SMTP
- servidor SMTP
- porta
- autenticação
- TLS/STARTTLS
- diferença entre aceitar uma mensagem para envio e entregá-la na caixa do destinatário

## Próximas etapas

1. Envio simples via SMTP ✅
2. Transformar o envio em uma API HTTP
3. Persistir e-mails e status
4. Introduzir processamento assíncrono
5. RabbitMQ: producer, queue e consumer
6. ACK/NACK e retry
7. Dead Letter Queue
8. Idempotência
9. Templates e anexos
10. Observabilidade e padrões de produção
