# FlowDesk

FlowDesk — учебный прототип сервис-деска для курсовой работы. Приложение позволяет клиентам создавать заявки, диспетчерам — назначать исполнителей и следить за SLA, исполнителям — вести работу по задачам, руководителям — смотреть отчёты, администраторам — настраивать категории, SLA-политики, правила маршрутизации и пользователей.

## Стек

- .NET 10, Blazor Web App (Interactive Server)
- ASP.NET Core Identity (`Guid`, роли, блокировка пользователей)
- Entity Framework Core 10 + PostgreSQL (Npgsql)
- MudBlazor 9
- MailKit (SMTP с fallback в файлы)
- QuestPDF (PDF-отчёты)
- Локальное файловое хранилище вложений

## Быстрый старт

### 1. Требования

- .NET SDK 10
- PostgreSQL 16+ (или Docker)
- `dotnet-ef` для миграций: `dotnet tool install --global dotnet-ef`

### 2. База данных

Вариант A — Docker:

```powershell
docker compose up -d
```

Вариант B — локальный кластер PostgreSQL:

```powershell
.\scripts\start-db.ps1
```

Скрипт создаёт кластер в `App_Data/pgdata` на порту `5433` с пользователем и базой `flowdesk` (trust-аутентификация, только для локальной разработки).

Вариант C — уже установленный PostgreSQL: создайте базу и пользователя `flowdesk` и при необходимости измените строку подключения в `appsettings.json`.

Строка подключения по умолчанию:

```text
Host=127.0.0.1;Port=5433;Database=flowdesk;Username=flowdesk;Password=flowdesk
```

### 3. Запуск

```powershell
dotnet restore
dotnet run --project FlowDesk.Web.csproj --urls http://127.0.0.1:5091
```

При старте приложение применяет миграции и заполняет демо-данные. Откройте <http://127.0.0.1:5091>.

Остановить локальный кластер:

```powershell
.\scripts\stop-db.ps1
```

## Демо-аккаунты

Пароль у всех аккаунтов: `Passw0rd!`

| Роль | Email |
| --- | --- |
| Клиент | `client@flowdesk.local` |
| Клиент | `client2@flowdesk.local` |
| Диспетчер | `dispatcher@flowdesk.local` |
| Исполнитель | `executor@flowdesk.local` |
| Исполнитель | `executor2@flowdesk.local` |
| Руководитель | `manager@flowdesk.local` |
| Администратор | `admin@flowdesk.local` |

## Возможности

- Регистрация клиента с автоматической ролью `Client` и полем «Имя и фамилия».
- Создание заявки: тема, описание, категория, приоритет.
- Расчёт дедлайна SLA по категории и приоритету; подсветка «в срок / риск / просрочено».
- Очередь диспетчера с фильтрами по статусу, категории, приоритету и поиском.
- Назначение и переназначение исполнителя с корректировкой дедлайна.
- Автоназначение по активному правилу маршрутизации при создании заявки.
- Статусы: «Новая», «Назначена», «В работе», «Выполнено», «Отклонено».
- Комментарии и вложения (до 10 МБ, белый список расширений).
- История изменений заявки.
- Уведомления в БД и email; при отсутствии SMTP письма сохраняются в `App_Data/emails`.
- Отчёты: сводка по статусам, категориям и приоритетам, среднее время выполнения, доля нарушений SLA.
- Экспорт отчётов в CSV и PDF.
- Администрирование: категории, SLA-политики, правила маршрутизации, активация/блокировка пользователей.
- Health-check: `GET /health`.

## Структура проекта

```text
FlowDesk.Web.csproj
Program.cs                     — DI, Identity, PostgreSQL, endpoints
Domain/                        — сущности и перечисления
Application/
  Contracts/                   — DTO и фильтры
  Interfaces.cs                — интерфейсы сервисов
  Services/                    — бизнес-логика заявок, SLA, отчётов, уведомлений
Infrastructure/                — email, файловое хранилище, сидер
Data/                          — DbContext и миграции
Components/
  Account/                     — страницы Identity
  Pages/                       — страницы по ролям
  Dialogs/                     — диалоги назначения и администрирования
  Shared/                      — чипы статусов, приоритетов и SLA
wwwroot/                       — статические ресурсы
```

## Настройки

`appsettings.json`:

- `ConnectionStrings:DefaultConnection` — PostgreSQL.
- `Email:Host` — SMTP-хост. Если пусто, письма пишутся в `App_Data/emails`.
- `FileStorage:Root` — каталог вложений (по умолчанию `App_Data/uploads`).
- `FileStorage:MaxFileSizeBytes` — максимальный размер файла.

Для реального SMTP задайте параметры через user secrets:

```powershell
dotnet user-secrets set "Email:Host" "smtp.example.com"
dotnet user-secrets set "Email:Port" "587"
dotnet user-secrets set "Email:UserName" "..."
dotnet user-secrets set "Email:Password" "..."
dotnet user-secrets set "Email:From" "flowdesk@example.com"
```

## Ограничения прототипа

- Локальное файловое хранилище вместо S3/Azure Blob.
- Уведомления в реальном времени не отправляются через SignalR; страницы обновляются вручную.
- Нет полноценного аудита входа и расширенной политики паролей.
- PDF-отчёт использует QuestPDF Community License.
- Приложение рассчитано на один экземпляр и не масштабируется горизонтально без внешнего хранилища файлов и распределённого кэша.
