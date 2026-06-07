# Stage 1: Build the React frontend
# Node 24 matches CI and satisfies Vite 8's engine (^20.19 || >=22.12); the old
# node:20-alpine pin was borderline against that requirement.
FROM node:24-alpine AS frontend-builder
WORKDIR /app
COPY package*.json ./
# Use a reproducible install from the lockfile.
RUN npm ci
COPY . .
RUN npm run build

# Stage 2: Build the .NET backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-builder
WORKDIR /src
COPY server/server.csproj ./server/
RUN dotnet restore server/server.csproj
COPY server/ ./server/
RUN dotnet publish server/server.csproj -c Release -o /app/publish

# Stage 3: Final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Copy built C# backend to /app/server
COPY --from=backend-builder /app/publish ./server
# Copy built React frontend to /app/dist
COPY --from=frontend-builder /app/dist ./dist

# Set working directory to /app/server to preserve the sibling directory layout (server/ and dist/)
WORKDIR /app/server

# Persist application data (db.json, sessions.json, data-protection keys) outside the
# container's ephemeral layer. On Render attach a persistent disk and set DATA_DIR to
# its mount path; otherwise this named volume keeps data across container restarts.
# Without persistence, ALL data (buses, users, settings) is lost on every redeploy.
ENV DATA_DIR=/app/data
VOLUME ["/app/data"]

# Expose port (Render will inject PORT environment variable)
ENV PORT=5001
EXPOSE 5001

ENTRYPOINT ["dotnet", "server.dll"]
