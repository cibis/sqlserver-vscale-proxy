# Use the official .NET Core SDK image as the base image
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy the .csproj and restore as distinct layers
ADD /NetProxy.API/ ./NetProxy.API
ADD /NetProxy.Core/ ./NetProxy.Core
RUN dotnet restore ./NetProxy.API/NetProxy.API.csproj

# Copy the remaining source code and build the application
COPY . ./
RUN dotnet publish ./NetProxy.API/NetProxy.API.csproj -c Release -o out #--self-contained true

# Build the runtime image
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/out .

# Entry point when the container starts
ENTRYPOINT ["./NetProxy.API"]