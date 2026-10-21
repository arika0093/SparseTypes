#!/usr/bin/env bash
# Verifies the packed Fragments package without repository project references.
set -euo pipefail

package_directory="${1:-}"
fixture_dir="tests/fixtures/consumers/package-sparse-fragments"
fixture_csproj="${fixture_dir}/PackageSparse.Fragments.Consumer.csproj"
success_marker="SparseTypes.Fragments packed consumer passed."

if [[ -z "${package_directory}" ]]; then
    echo "Usage: $(basename "$0") <package-directory>" >&2
    exit 2
fi
if [[ ! -f "${fixture_csproj}" ]]; then
    echo "Fragments package consumer fixture missing at '${fixture_csproj}'." >&2
    exit 1
fi

package="$(ls "${package_directory}"/SparseTypes.Fragments.[0-9]*.nupkg 2>/dev/null | head -n 1 || true)"
if [[ -z "${package}" ]]; then
    echo "No SparseTypes.Fragments package found in '${package_directory}'." >&2
    exit 1
fi
version="$(basename "${package}" | sed -E 's/^SparseTypes\.Fragments\.(.+)\.nupkg$/\1/')"
if [[ -z "${version}" ]]; then
    echo "Could not resolve package version from '$(basename "${package}")'." >&2
    exit 1
fi
feed="$(realpath "${package_directory}")"

work="$(mktemp -d)"
isolated_packages="$(mktemp -d)"
trap 'rm -rf "${work}" "${isolated_packages}"' EXIT
if command -v cygpath >/dev/null 2>&1; then
    export NUGET_PACKAGES="$(cygpath -w "${isolated_packages}")"
else
    export NUGET_PACKAGES="${isolated_packages}"
fi
cp "${fixture_csproj}" "${fixture_dir}/Program.cs" "${work}/"

dotnet build "${work}/PackageSparse.Fragments.Consumer.csproj" \
    --configuration Release \
    -p:SparseTypesFragmentsPackageVersion="${version}" \
    -p:RestoreAdditionalProjectSources="${feed}"

assets_file="${work}/obj/project.assets.json"
if [[ ! -f "${assets_file}" ]]; then
    echo "Restore did not produce '${assets_file}'." >&2
    exit 1
fi
if ! grep -F -q "SparseTypes.Fragments/${version}" "${assets_file}"; then
    echo "Isolated restore did not resolve 'SparseTypes.Fragments/${version}'." >&2
    exit 1
fi
if ! grep -F -q "SparseTypes.Core/" "${assets_file}"; then
    echo "Fragments restore did not include its SparseTypes.Core dependency." >&2
    exit 1
fi
if grep -F -q '"SparseTypes/' "${assets_file}"; then
    echo "Fragments restore unexpectedly depends on the full SparseTypes package." >&2
    exit 1
fi
if grep -F -q '"System.Text.Json/' "${assets_file}"; then
    echo "Fragments restore unexpectedly depends on System.Text.Json." >&2
    exit 1
fi
lower_version="$(printf '%s' "${version}" | tr '[:upper:]' '[:lower:]')"
if [[ ! -d "${isolated_packages}/sparsetypes.fragments/${lower_version}" ]]; then
    echo "Isolated global-packages did not extract the Fragments candidate package." >&2
    exit 1
fi
if [[ ! -d "${isolated_packages}/sparsetypes.core/${lower_version}" ]]; then
    echo "Isolated global-packages did not extract the Core dependency package." >&2
    exit 1
fi

consumer_dll="${work}/bin/Release/net10.0/PackageSparse.Fragments.Consumer.dll"
output="$(dotnet "${consumer_dll}" 2>&1)"
echo "${output}"
if [[ "${output}" != *"${success_marker}"* ]]; then
    echo "Fragments package consumer did not reach its success marker." >&2
    exit 1
fi
if [[ -e "${work}/bin/Release/net10.0/SparseTypes.dll" || -e "${work}/bin/Release/net10.0/SparseTypes.Generator.dll" ]]; then
    echo "Fragments consumer output contains the full runtime or generator." >&2
    exit 1
fi

echo "SparseTypes.Fragments packed package consumer verified (${version})."
