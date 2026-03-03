# Решение проблем с проектом Bebochka.TelegramBot

## Ошибка NU1105: Не удалось найти сведения о проекте

Если вы видите ошибку:
```
NU1105	Не удалось найти сведения о проекте для "E:\Project\React\bebochka\backend\Bebochka.Api\Bebochka.Api.csproj"
```

### Решение 1: Восстановление пакетов через командную строку

Откройте командную строку или PowerShell в корне проекта и выполните:

```bash
cd backend\Bebochka.Api
dotnet restore

cd ..\..\Bebochka.TelegramBot
dotnet restore
```

### Решение 2: Перезагрузка решения в Visual Studio

1. Закройте Visual Studio
2. Откройте `backend\Bebochka.Api\Bebochka.Api.sln`
3. В Solution Explorer убедитесь, что оба проекта загружены:
   - Bebochka.Api
   - Bebochka.TelegramBot
4. Правой кнопкой на решении → "Restore NuGet Packages"
5. Build → Clean Solution
6. Build → Rebuild Solution

### Решение 3: Проверка пути к проекту

Убедитесь, что путь к проекту правильный в `Bebochka.TelegramBot.csproj`:

```xml
<ProjectReference Include="..\backend\Bebochka.Api\Bebochka.Api.csproj" />
```

Относительный путь от `Bebochka.TelegramBot` к `Bebochka.Api`:
- `Bebochka.TelegramBot` находится в: `E:\Project\React\bebochka\Bebochka.TelegramBot\`
- `Bebochka.Api` находится в: `E:\Project\React\bebochka\backend\Bebochka.Api\`
- Путь: `..\backend\Bebochka.Api\Bebochka.Api.csproj` ✓

### Решение 4: Удаление папок bin и obj

Иногда помогает удаление папок `bin` и `obj`:

```bash
# В PowerShell
Remove-Item -Recurse -Force Bebochka.TelegramBot\bin, Bebochka.TelegramBot\obj
Remove-Item -Recurse -Force backend\Bebochka.Api\bin, backend\Bebochka.Api\obj

# Затем восстановите
dotnet restore backend\Bebochka.Api\Bebochka.Api.csproj
dotnet restore Bebochka.TelegramBot\Bebochka.TelegramBot.csproj
```

### Решение 5: Проверка версии .NET SDK

Убедитесь, что установлен .NET 8.0 SDK:

```bash
dotnet --version
```

Должно быть версия 8.0.x или выше.

### Решение 6: Перемещение проекта в папку backend

Если ничего не помогает, можно переместить проект в папку `backend` для единообразия структуры:

1. Переместите папку `Bebochka.TelegramBot` в `backend\`
2. Обновите путь в `Bebochka.TelegramBot.csproj`:
   ```xml
   <ProjectReference Include="..\Bebochka.Api\Bebochka.Api.csproj" />
   ```
3. Обновите путь в `Bebochka.Api.sln`:
   ```
   Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Bebochka.TelegramBot", "..\Bebochka.TelegramBot\Bebochka.TelegramBot.csproj", "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
   ```

