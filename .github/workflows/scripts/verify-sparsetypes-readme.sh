#!/usr/bin/env bash
# Verifies the standalone SparseTypes README samples.
set -euo pipefail

package_directory="${1:-}"
readme_path="${2:-README.md}"
fixture_dir="tests/fixtures/consumers/package-sparse-readme"
fixture_csproj="${fixture_dir}/PackageSparse.Readme.Consumer.csproj"
fixture_program="${fixture_dir}/ReadmeSamples.cs"

if [[ -z "${package_directory}" ]]; then
    echo "Usage: $(basename "$0") <package-directory> [readme-path]" >&2
    exit 2
fi
if [[ ! -f "${readme_path}" ]]; then
    echo "README file '${readme_path}' does not exist." >&2
    exit 1
fi
if [[ ! -f "${fixture_csproj}" || ! -f "${fixture_program}" ]]; then
    echo "Canonical README fixture missing under '${fixture_dir}'." >&2
    exit 1
fi

# Required API tokens are checked in both the README and fixture.
# Advanced APIs are covered by the docs sample consumers.
required_tokens=(
    '[SparseModel]'
    'public partial class Order'
    'CreateEditSession'
    'session.Observable'
    'session.HasChanges'
    'CreateChangeSet'
    'changes.CustomerName.Before.Value'
    'changes.CustomerName.After.Value'
    'changes.Quantity.IsChanged'
    'session.RevertChanges'
)
for token in "${required_tokens[@]}"; do
    if ! grep -F -q "${token}" "${readme_path}"; then
        echo "README '${readme_path}' is missing required sample token '${token}'." >&2
        exit 1
    fi
    if ! grep -F -q "${token}" "${fixture_program}"; then
        echo "Canonical fixture '${fixture_program}' is missing token '${token}' present in the README; keep them in sync." >&2
        exit 1
    fi
done

# Verify README samples against their registered canonical fixtures.
if ! python3 "$(dirname "$0")/check-docs-samples.py" --registry "$(dirname "$0")/docs-samples-registry.tsv" --only "${readme_path}"; then
    echo "README sample drift: see mismatches above." >&2
    exit 1
fi

# Compile and run the canonical fixture against the packed core package.
package="$(ls "${package_directory}"/SparseTypes.[0-9]*.nupkg 2>/dev/null | head -n 1 || true)"
if [[ -z "${package}" ]]; then
    echo "No SparseTypes package found in '${package_directory}'." >&2
    exit 1
fi
version="$(basename "${package}" | sed -E 's/^SparseTypes\.(.+)\.nupkg$/\1/')"
if [[ -z "${version}" ]]; then
    echo "Could not resolve package version from '$(basename "${package}")'." >&2
    exit 1
fi
feed="$(realpath "${package_directory}")"

# Use a fresh cache to avoid stale packages with the same version.
isolated_packages="$(mktemp -d)"
generated_snapshot="$(mktemp -d)"
compiler_generated="$(mktemp -d)"
cp -a docs/generated-code/. "${generated_snapshot}/"
trap 'rm -rf "${isolated_packages}" "${generated_snapshot}" "${compiler_generated}"' EXIT
if command -v cygpath >/dev/null 2>&1; then
    export NUGET_PACKAGES="$(cygpath -w "${isolated_packages}")"
else
    export NUGET_PACKAGES="${isolated_packages}"
fi
lower_version="$(printf '%s' "${version}" | tr '[:upper:]' '[:lower:]')"

framework="net10.0"
dotnet build "${fixture_csproj}" \
    --configuration Release --framework "${framework}" \
    -p:SparseTypesPackageVersion="${version}" \
    -p:CompilerGeneratedFilesOutputPath="${compiler_generated}" \
    -p:ReadmeGeneratedCodeDirectory="${generated_snapshot}" \
    --no-incremental \
    -p:RestoreAdditionalProjectSources="${feed}"
assets_file="${fixture_dir}/obj/project.assets.json"
if [[ ! -f "${assets_file}" ]]; then
    echo "Restore did not produce '${assets_file}'." >&2
    exit 1
fi
if ! grep -F -q "SparseTypes/${version}" "${assets_file}"; then
    echo "Isolated restore did not resolve 'SparseTypes/${version}' (see '${assets_file}')." >&2
    exit 1
fi
if [[ ! -d "${isolated_packages}/sparsetypes/${lower_version}" ]]; then
    echo "Isolated global-packages has no 'sparsetypes/${lower_version}': restore did not extract the candidate nupkg." >&2
    exit 1
fi
output="$(dotnet "${fixture_dir}/bin/Release/${framework}/PackageSparse.Readme.Consumer.dll" 2>&1)"
echo "${output}"
if [[ "${output}" != *"SparseTypes README consumer passed."* ]]; then
    echo "SparseTypes README fixture did not reach its success marker on '${framework}'." >&2
    exit 1
fi

if ! diff -ru docs/generated-code "${generated_snapshot}"; then
    echo "README generated-code snapshots are out of date. Regenerate them with:" >&2
    echo "  dotnet build ${fixture_csproj} --configuration Release --framework ${framework} -t:RegenerateReadmeSnapshot" >&2
    exit 1
fi

echo "SparseTypes README samples verified (SparseTypes ${version})."
