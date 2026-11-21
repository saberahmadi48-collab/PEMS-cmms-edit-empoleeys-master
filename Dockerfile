# ==========================================================================
# PEMS.Web - Multi-stage Dockerfile
# طبق PEMS Architecture & Development Bible (Volume X - DevOps):
# "Cloud Ready / On-Premise Ready" - این فایل فقط بسته‌بندی می‌کند، هیچ کد
# برنامه را تغییر نمی‌دهد.
# ==========================================================================

# ---- مرحله ۱: Build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# ابتدا فقط فایل‌های csproj برای استفاده از Cache لایه‌های Docker در Build های بعدی
COPY PEMS.Domain/*.csproj PEMS.Domain/
COPY PEMS.Application/*.csproj PEMS.Application/
COPY PEMS.Persistence/*.csproj PEMS.Persistence/
COPY PEMS.Migrations/*.csproj PEMS.Migrations/
COPY PEMS.Infrastructure/*.csproj PEMS.Infrastructure/
COPY PEMS.AI/*.csproj PEMS.AI/
COPY PEMS.Shared/*.csproj PEMS.Shared/
COPY PEMS.WorkflowPlatform/*.csproj PEMS.WorkflowPlatform/
COPY PEMS.Licensing/*.csproj PEMS.Licensing/
COPY PEMS.UI.Abstractions/*.csproj PEMS.UI.Abstractions/
COPY PEMS.UI/*.csproj PEMS.UI/
COPY PEMS.Web/*.csproj PEMS.Web/
COPY PEMS.Tests/*.csproj PEMS.Tests/
COPY *.slnx ./

RUN dotnet restore PEMS.Web/PEMS.Web.csproj

# حالا بقیه‌ی سورس
COPY . .

WORKDIR /src/PEMS.Web
RUN dotnet publish PEMS.Web.csproj -c Release -o /app/publish --no-restore

# ---- مرحله ۲: Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Tesseract OCR (برای PEMS.AI) - لازم است روی Image خودتان اگر پردازش OCR فعال است نصب شود
RUN apt-get update \
    && apt-get install -y --no-install-recommends tesseract-ocr tesseract-ocr-eng tesseract-ocr-fas \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Non-secret settings files (env/*.env). env/*.secrets.env is excluded by .dockerignore; choose the environment at
# run time with PEMS_ENV (docker-compose.yml sets it) and provide secrets via env_file/environment variables.
COPY env/*.env /app/env/

# پوشه‌ی آپلود فایل‌ها و Intake Queue باید Persistent باشد - در docker-compose.yml به یک Volume وصل می‌شود
RUN mkdir -p /app/wwwroot/uploads

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "PEMS.Web.dll"]
