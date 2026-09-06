interface OpenEnumTarget {
  readonly file: string;
  readonly names: readonly string[];
}

const generatedPath = process.argv[2];
if (!generatedPath) {
  throw new Error("Pass the generated TypeScript source directory.");
}

const openEnumTargets: readonly OpenEnumTarget[] = [
  {file: "models/ConnectorType.ts", names: ["ConnectorType"]},
  {file: "models/ConversationCategory.ts", names: ["ConversationCategory"]},
  {file: "models/ConversationStatus.ts", names: ["ConversationStatus"]},
  {file: "models/DeliveryStatus.ts", names: ["DeliveryStatus"]},
  {file: "models/IntakeCategory.ts", names: ["IntakeCategory"]},
  {file: "models/MessageRole.ts", names: ["MessageRole"]},
  {
    file: "models/ModelInvocationError.ts",
    names: ["ModelInvocationErrorCategoryEnum"],
  },
  {
    file: "models/Report.ts",
    names: ["ReportStatusReasonEnum", "ReportSubmissionOriginEnum"],
  },
  {file: "models/ReportStatus.ts", names: ["ReportStatus"]},
  {file: "models/WriterModelKey.ts", names: ["WriterModelKey"]},
];

for (const target of openEnumTargets) {
  const path = `${generatedPath}/${target.file}`;
  let source = await Bun.file(path).text();

  for (const name of target.names) {
    const closedDeclaration = `export type ${name} = typeof ${name}[keyof typeof ${name}];`;
    const occurrenceCount = source.split(closedDeclaration).length - 1;
    if (occurrenceCount !== 1) {
      throw new Error(
        `Expected one generated declaration for ${name}, found ${occurrenceCount}.`,
      );
    }
    source = source.replace(
      closedDeclaration,
      `export type ${name} = (typeof ${name})[keyof typeof ${name}] | (string & {});`,
    );
  }

  await Bun.write(path, source);
}
