import * as core from "oci-core";

console.log("\n=== ENUMS EM core.requests ===\n");

Object.entries(core.requests).forEach(
  ([name, value]) => {
    if (
      value &&
      typeof value === "object" &&
      Object.keys(value).length > 0
    ) {
      console.log(`\n📦 ${name}:`);
      Object.entries(value).forEach(
        ([enumName, enumValue]) => {
          console.log(
            `   .${enumName} =`,
            JSON.stringify(enumValue)
          );
        }
      );
    }
  }
);