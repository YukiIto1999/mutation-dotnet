{ pkgs, ... }:

{
  packages = [
    pkgs.dotnet-sdk_10
    pkgs.just
    pkgs.git
    pkgs.jq
  ];

  env = {
    DOTNET_CLI_TELEMETRY_OPTOUT = "1";
    DOTNET_NOLOGO = "1";
  };

  scripts.verify.exec = ''
    set -euo pipefail

    dotnet build Mutation.slnx
    dotnet tests/Mutation.Tests/bin/Debug/net10.0/Mutation.Tests.dll --no-ansi --disable-logo --no-progress
    bash tests/e2e/medium/verification/verify.sh
    bash tests/e2e/medium/frameworks/verify-frameworks.sh

    # 自己適用の mutation ゲート
    dotnet src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll run \
      --project src/Mutation/Mutation.csproj \
      --test-project tests/Mutation.Tests/Mutation.Tests.csproj \
      --output .mutation-output/self \
      --exclude-static --with-baseline --break-at 25
  '';
}
