import argparse
from pathlib import Path

from geocode_via_cep import (
    cep_aberto_token,
    get_coordinates_from_cep_aberto,
    get_coordinates_from_mapbox,
    valid_coordinates,
)
from import_common import normalize_ibge_code, validate_uf
from neighborhood_snapshot import read_snapshot, write_snapshot
from neighborhood_source_import import (
    build_dne_street_index,
    connect_dne,
    municipality_lookup,
)


def _require_token():
    token = cep_aberto_token()
    if not token:
        raise RuntimeError("Defina CEP_ABERTO_API_TOKEN antes de executar")
    return token


def _municipalities_from_rows(rows):
    return [{"nome": row["City"], "id": row["CityIbgeCode"]} for row in rows]


def _coordinates_present(row):
    return valid_coordinates(row.get("Latitude"), row.get("Longitude"))


def _validate_rows_belong_to_uf(rows, uf):
    for row in rows:
        row_uf = validate_uf(row.get("Uf"))
        if row_uf != uf:
            raise ValueError(
                f'Linha com UF inesperada: {row.get("City")}/{row.get("Neighborhood")} '
                f'usa {row_uf}, esperado {uf}'
            )


def regeocode_rows(rows, uf, street_index):
    """Preenche coordenadas pendentes de linhas de snapshot via Cep Aberto/Mapbox.

    Preserva linhas ja geolocalizadas sem tocar nos valores; linhas sem CEP no
    indice permanecem pendentes; linhas com CEP cujo Cep Aberto nao retorna um
    par valido tentam o Mapbox como fallback, registrando ``mapbox_geocoding``;
    quando nenhum dos dois retorna um par valido, a linha permanece pendente.
    Erros individuais do provedor (403/429/5xx ou timeout) sao capturados por
    linha: a linha permanece pendente e o processamento continua. Retorna
    ``(total, recuperados, sem_cep, pendentes, provider_errors)``.
    """
    uf = validate_uf(uf)
    _validate_rows_belong_to_uf(rows, uf)

    total = len(rows)
    recuperados = 0
    sem_cep = 0
    provider_errors = 0
    for row in rows:
        if _coordinates_present(row):
            continue
        ibge = normalize_ibge_code(row.get("CityIbgeCode"))
        street, cep = street_index.get((ibge, row.get("NormalizedName")), (None, None))
        if not cep:
            sem_cep += 1
            continue
        latitude = longitude = None
        try:
            latitude, longitude = get_coordinates_from_cep_aberto(cep)
        except Exception:
            provider_errors += 1
        source = None
        if valid_coordinates(latitude, longitude):
            source = "cep_aberto"
        else:
            try:
                latitude, longitude = get_coordinates_from_mapbox(
                    street, row.get("City"), row.get("Uf"), cep
                )
            except Exception:
                provider_errors += 1
            if valid_coordinates(latitude, longitude):
                source = "mapbox_geocoding"
        if source is not None:
            row["Latitude"] = latitude
            row["Longitude"] = longitude
            row["Source"] = source
            recuperados += 1

    pendentes = sum(1 for row in rows if not _coordinates_present(row))
    return total, recuperados, sem_cep, pendentes, provider_errors


def regeocode_file(file_path, uf, output_path):
    """Repara coordenadas pendentes de um snapshot CSV validado.

    Exige `CEP_ABERTO_API_TOKEN` antes de tocar em qualquer arquivo; le o
    snapshot validado, garante que todas as linhas pertencem a UF, monta o
    lookup de municipios a partir das linhas e carrega o indice DNE uma unica
    vez. Grava o CSV de saida de forma atomica e validada.
    """
    _require_token()
    uf = validate_uf(uf)
    rows = read_snapshot(file_path)
    _validate_rows_belong_to_uf(rows, uf)

    lookup_by_name, lookup_by_code = municipality_lookup(_municipalities_from_rows(rows))

    connection = connect_dne()
    try:
        street_index = build_dne_street_index(connection, uf, lookup_by_name, lookup_by_code)
    finally:
        connection.close()

    total, recuperados, sem_cep, pendentes, provider_errors = regeocode_rows(
        rows, uf, street_index
    )

    write_snapshot(rows, output_path)
    return {
        "total": total,
        "recuperados": recuperados,
        "pendentes": pendentes,
        "sem_cep": sem_cep,
        "provider_errors": provider_errors,
    }


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Repara coordenadas pendentes de um snapshot CSV via Cep Aberto e Mapbox"
    )
    parser.add_argument("--uf", type=validate_uf, required=True)
    parser.add_argument("--file", type=Path, required=True)
    parser.add_argument(
        "--output",
        type=Path,
        default=None,
        help="Arquivo CSV de saida (padrao: sobrescreve --file)",
    )
    args = parser.parse_args(argv)

    output = args.output or args.file
    report = regeocode_file(args.file, args.uf, output)
    print(
        f"UF {args.uf}: {report['total']} totais; {report['recuperados']} recuperados; "
        f"{report['pendentes']} pendentes ({report['sem_cep']} sem CEP); "
        f"{report['provider_errors']} erros do provedor; CSV: {output}"
    )


if __name__ == "__main__":
    main()
