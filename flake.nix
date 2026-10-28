{
  description = "mdex-tooling – Gateway & Token CLI";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = nixpkgs.legacyPackages.${system};
        dotnet = pkgs.dotnetCorePackages;

        # deps.json (NuGet-Lockdatei) neu erzeugen nach Paket-Updates:
        #   nix build .#mdex-gateway.fetch-deps && ./result deps.json

        # --- mdex-gateway (ASP.NET Core web app) ---
        gateway = pkgs.buildDotnetModule {
          pname = "mdex-gateway";
          version = "0.1.0";
          src = ./src;

          projectFile = "Mdex.Gateway/Mdex.Gateway.csproj";
          dotnet-sdk = dotnet.sdk_10_0;
          dotnet-runtime = dotnet.aspnetcore_10_0;
          nugetDeps = ./deps.json;
          executables = [ "mdex-gateway" ];
          # commit-and-push ruft git auf
          makeWrapperArgs = [ "--prefix" "PATH" ":" (pkgs.lib.makeBinPath [ pkgs.git ]) ];
        };

        # --- mdex-token (CLI) ---
        token = pkgs.buildDotnetModule {
          pname = "mdex-token";
          version = "0.1.0";
          src = ./src;

          projectFile = "Mdex.Token/Mdex.Token.csproj";
          dotnet-sdk = dotnet.sdk_10_0;
          dotnet-runtime = dotnet.runtime_10_0;
          nugetDeps = ./deps.json;
          executables = [ "mdex-token" ];
        };

      in
      {
        packages = {
          default = gateway;
          mdex-gateway = gateway;
          mdex-token = token;
        };
      }
    );
}
