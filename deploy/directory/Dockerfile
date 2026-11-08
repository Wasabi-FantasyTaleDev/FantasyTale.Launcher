FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN mkdir /data && chown app:app /data
COPY --chown=app:app publish/ ./
USER app
ENV FT_DIRECTORY_Listen=http://0.0.0.0:8096
ENV FT_DIRECTORY_DataPath=/data
ENTRYPOINT ["dotnet", "FantasyTale.Launcher.Directory.dll"]
