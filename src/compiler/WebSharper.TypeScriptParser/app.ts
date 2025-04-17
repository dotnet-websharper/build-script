﻿import * as fs from "fs"
import * as ts from "typescript"
import * as readline from "readline"

// main: parse and transform file

let filePath = process.argv[2]
if (filePath == null)
  throw "Please provide the path to a TypeScript definition file"

let program = ts.createProgram([filePath], { target: ts.ScriptTarget.Latest });
let checker = program.getTypeChecker();

let output =
  program.getSourceFiles()
    .filter(f => f.fileName.includes("/lib."))
//    .filter(f => !program.isSourceFileDefaultLibrary(f) && !program.isSourceFileFromExternalLibrary(f))
    .map(transformFile)

let outputPath = process.argv[3] ?? (filePath + ".json")

console.log("Writing output to: ", outputPath)
fs.writeFileSync(outputPath, JSON.stringify(output, undefined, 2))

const rl = readline.createInterface({
  input: process.stdin,
  output: process.stdout
});

rl.on('line', () => {
  process.exit(0)
});

//process.exit(0)

// output shape

interface TSParameter {
  Name: string
  Type: TSType
}
interface TSSimpleType {
  Kind: 'simple'
  Type: string
}
interface TSLiteralType {
  Kind: 'literal'
  Value: string
}
interface TSArrayType {
  Kind: 'array'
  ElementType: TSType
}
interface TSTupleType {
  Kind: 'tuple'
  ElementTypes: TSType[]
}
interface TSFunctionOrNewType {
  Kind: 'function' | 'new'
  Name?: string
  Parameters: TSParameter[]
  ReturnType: TSType
}
interface TSUnionOrIntersectionType {
  Kind: 'union' | 'intersection'
  Types: TSType[]
}
interface TSConditionalType {
  Kind: 'conditional'
  CheckType: TSType
  ExtendsType: TSType
  TrueType: TSType
  FalseType: TSType
}
interface TSTypeLiteral {
  Kind: 'object'
  Members: TSTypeElement[]
}
interface TSTypeReference {
  Kind: 'typeref'
  Type: string
  Arguments?: TSType[]
}
interface TSTypeParamReference {
  Kind: 'typeparamref'
  Type: string
}
interface TSTypePredicate {
  Kind: 'predicate'
  Type: TSType
  Parameter: string
}
interface TSIndexType {
  Kind: 'index'
  Index: TSType
  Type: TSType
}
interface TSKeyOfOrMappedType {
  Kind: 'keyof' | 'mapped'
  Type: TSType
}
interface TSTypeQuery {
  Kind: 'query'
  Expression: string
}
type TSType =
  | TSSimpleType
  | TSLiteralType
  | TSArrayType
  | TSTupleType
  | TSFunctionOrNewType
  | TSUnionOrIntersectionType
  | TSConditionalType
  | TSTypeLiteral
  | TSTypeReference
  | TSTypeParamReference
  | TSTypePredicate
  | TSIndexType
  | TSKeyOfOrMappedType
  | TSTypeQuery 
interface TSTypeParameter {
  Name: string
  Constraint?: TSType
}
interface TSTypeElement {
  Kind: 'method' | 'property' | 'new' | 'call' | 'get' | 'set' | 'index'
  Name?: string
  Static?: boolean
  Parameters?: TSParameter[]
  TypeParameters?: TSTypeParameter[]
  Type?: TSType
}
interface TSVariableStatement {
  Kind: 'vars'
  Declarations: TSParameter[]
}
interface TSTypeAlias {
  Kind: 'typealias'
  Name: string
  TypeParameters?: TSTypeParameter[]
  Type: TSType
}
interface TSTypeDeclaration {
  Kind: 'class' | 'interface'
  Name: string
  TypeParameters?: TSTypeParameter[]
  Members: TSTypeElement[]
  Extends?: TSType | TSType[]
  Implements?: TSType[]
}
interface TSModuleDeclaration {
  Kind: 'module'
  Name: string
  Members: TSStatement[]
}
interface TSExportAssignment {
  Kind: 'exportassignment'
  Expression: string
}
interface TSExportDeclaration {
  Kind: 'exportdeclaration'
  Expression: string
  Name?: string
}
type TSStatement =
  | TSVariableStatement
  | TSFunctionOrNewType
  | TSTypeAlias
  | TSTypeDeclaration
  | TSModuleDeclaration
  | TSExportAssignment
  | TSExportDeclaration

interface TSFile {
    Name: string
    Statements: TSStatement[]
}

// transformer functions

function transformParameter(p: ts.ParameterDeclaration): TSParameter {
  return {
    Name: p.name.getText(),
    Type: transformType(p.type)
  }
}

function simpleType(x: string): TSSimpleType {
  return {
    Kind: 'simple',
    Type: x
  }
}

function unhandled(x: ts.Node, ctx: string) {
  throw Error(`Unhandled SyntaxKind for ${ctx}: ${x.kind} '${x.getText()}' in ${x.getSourceFile().fileName}`);
}

function transformType(x: ts.TypeNode): TSType {
  if (!x) return simpleType('any');
  if (ts.isParenthesizedTypeNode(x))
    return transformType(x.type)
  if (ts.isArrayTypeNode(x))
    return {
      Kind: 'array',
      ElementType: transformType(x.elementType)
    }
  if (ts.isTupleTypeNode(x))
    return {
      Kind: 'tuple',
      ElementTypes: x.elements.map(transformType)
    }
  if (ts.isFunctionTypeNode(x))
    return {
      Kind: 'function',
      Name: x.name?.getText(),
      Parameters: x.parameters.map(transformParameter),
      ReturnType: transformType(x.type)
    }
  if (ts.isConstructorTypeNode(x))
    return {
      Kind: 'new',
      Name: x.name?.getText(),
      Parameters: x.parameters.map(transformParameter),
      ReturnType: transformType(x.type)
    }
  if (ts.isUnionTypeNode(x))
    return {
      Kind: 'union',
      Types: x.types.map(transformType)
    }
  if (ts.isIntersectionTypeNode(x))
    return {
      Kind: 'intersection',
      Types: x.types.map(transformType)
    }
  if (ts.isConditionalTypeNode(x))
    return {
      Kind: 'conditional',
      CheckType: transformType(x.checkType),
      ExtendsType: transformType(x.extendsType),
      TrueType: transformType(x.trueType),
      FalseType: transformType(x.falseType)
    }
  if (ts.isTypeLiteralNode(x))
    return {
      Kind: 'object',
      Members: x.members.map(transformTypeElement)
    }
  if (ts.isIndexedAccessTypeNode(x))
    return {
      Kind: 'index',
      Index: transformType(x.indexType),
      Type: transformType(x.objectType)
    }
  if (ts.isTypeReferenceNode(x)) {
    let t = checker.getTypeAtLocation(x)
    let n = x.typeName.getText()
    let s = t.aliasSymbol || t.symbol
    if (x.typeArguments)
      return {
        Kind: 'typeref',
        Type: n,
        Arguments: x.typeArguments.map(transformType)
      }
    else {
      if (t && t.flags & ts.TypeFlags.TypeParameter)
        return {
          Kind: 'typeparamref',
          Type: n
        }
      else { 
        if (s && !(s.flags & ts.SymbolFlags.TypeLiteral))
          n = checker.getFullyQualifiedName(s)
        if ((<any>t).intrinsicName) // for aliases of built-in types
          n = (<any>t).intrinsicName
        return simpleType(n)
      }
    }
  }
  if (ts.isTypePredicateNode(x))
    return {
      Kind: 'predicate',
      Type: transformType(x.type),
      Parameter: x.parameterName.getText()
    }
  if (ts.isTypeOperatorNode(x)) {
    if (x.operator == ts.SyntaxKind.KeyOfKeyword)
      return {
        Kind: 'keyof',
        Type: transformType(x.type)
      }
    return transformType(x.type);
  }
  if (ts.isMappedTypeNode(x))
    return {
      Kind: 'mapped',
      Type: transformType(x.type)
    }
  if (ts.isTypeQueryNode(x)) {
    return {
      Kind: 'query',
      Expression: x.exprName.getText()
    }
  }
  let res = x.getText()
  if (ts.isImportTypeNode(x)) {
    return simpleType(res) // TODO
  }
  if (ts.isInferTypeNode(x)) {
    return simpleType(res) // TODO
  }
  if (ts.isLiteralTypeNode(x)) {
    return {
      Kind: 'literal',
      Value: res
    }
  }
  if (ts.isOptionalTypeNode(x)) {
    return simpleType(res) // TODO
  }
  if (ts.isRestTypeNode(x)) {
    return simpleType(res) // TODO
  }
  if (ts.isTemplateLiteralTypeNode(x)) {
    return simpleType(res) // TODO
  }
  if (ts.isThisTypeNode(x)) {
    return simpleType(res) // TODO
  }
  if (ts.isTypeOfExpression(x)) {
    return simpleType(res) // TODO
  }
  if (res.indexOf('<') > 0)
    unhandled(x, "TypeNode");
  return simpleType(res)
}

function transformTypeElement(x: ts.TypeElement): TSTypeElement {
  if (ts.getJSDocDeprecatedTag(x) != null)
    return null;
  if (ts.isMethodSignature(x))
    return {
      Kind: 'method',
      Name: x.name.getText(),
      Parameters: x.parameters.map(transformParameter),
      Type: transformType(x.type)
    }
  if (ts.isPropertySignature(x))
    return {
      Kind: 'property',
      Name: x.name.getText(),
      Type: transformType(x.type)
    }
  if (ts.isConstructSignatureDeclaration(x))
    return {
      Kind: 'new',
      Parameters: x.parameters.map(transformParameter),
      Type: transformType(x.type)
    }
  if (ts.isCallSignatureDeclaration(x))
    return {
      Kind: 'call',
      Parameters: x.parameters.map(transformParameter),
      Type: transformType(x.type)
    }
  if (ts.isGetAccessorDeclaration(x))
    return {
      Kind: 'get',
      Name: x.name.getText(),
      Type: transformType(x.type)
    }
  if (ts.isSetAccessorDeclaration(x))
    return {
      Kind: 'set',
      Name: x.name.getText(),
      Type: transformType(x.type)
    }
  if (ts.isIndexSignatureDeclaration(x))
    return {
      Kind: 'index',
      Parameters: x.parameters.map(transformParameter),
      Type: transformType(x.type)
    }
  unhandled(x, "TypeElement");
}

function transformTypeParameter(x: ts.TypeParameterDeclaration): TSTypeParameter {
  return {
    Name: x.name.getText(),
    Constraint: x.constraint && transformType(x.constraint)
  }
}

function transformClassElement(x: ts.ClassElement): TSTypeElement {
  if (ts.getJSDocDeprecatedTag(x) != null)
    return null;
  if (ts.isMethodDeclaration(x))
    return {
      Kind: 'method',
      Name: x.name.getText(),
      Static: x.modifiers.some(m => m.kind == ts.SyntaxKind.AbstractKeyword),
      Parameters: x.parameters.map(transformParameter),
      TypeParameters: x.typeParameters?.map(transformTypeParameter),
      Type: transformType(x.type)
    }
  if (ts.isConstructorDeclaration(x))
    return {
      Kind: 'new',
      Parameters: x.parameters.map(transformParameter),
      Type: transformType(x.type)
    }
  if (ts.isPropertyDeclaration(x))
    return {
      Kind: 'property',
      Name: x.name.getText(),
      Static: x.modifiers.some(m => m.kind == ts.SyntaxKind.AbstractKeyword),
      Type: transformType(x.type)
    }
  unhandled(x, "ClassElement");
}

function transformExpessionWithTypeArguments(x: ts.ExpressionWithTypeArguments): TSType {
  if (x.typeArguments)
    return {
      Kind: 'typeref',
      Type: x.expression.getText(),
      Arguments: x.typeArguments?.map(transformType)
    }
  else
    return simpleType(x.expression.getText())
}

function transformStatement(x: ts.Statement): TSStatement {
  //if (ts.getJSDocDeprecatedTag(x) != null)
  //  return null;
  if (ts.isVariableStatement(x))
    return {
      Kind: 'vars',
      Declarations: x.declarationList.declarations.map(d => ({ Name: d.name.getText(), Type: transformType(d.type) }))
    }
  if (ts.isFunctionDeclaration(x))
    return {
      Kind: 'function',
      Name: x.name?.getText(),
      Parameters: x.parameters.map(transformParameter),
      ReturnType: transformType(x.type)
    }
  if (ts.isInterfaceDeclaration(x))
    return {
      Kind: 'interface',
      Name: x.name.text,
      Members: x.members.map(transformTypeElement).filter(x => x != null),
      Extends: x.heritageClauses && x.heritageClauses[0].types.map(transformExpessionWithTypeArguments)
    }
  if (ts.isClassDeclaration(x)) {
    let ext: TSType[] =
      x.heritageClauses &&
      [].concat([],
        x.heritageClauses.filter(c => c.token == ts.SyntaxKind.ExtendsKeyword).map(c => c.types.map(transformExpessionWithTypeArguments))
      )
    let impl =
      x.heritageClauses &&
      [].concat([],
        x.heritageClauses.filter(c => c.token == ts.SyntaxKind.ImplementsKeyword).map(c => c.types.map(transformExpessionWithTypeArguments))
      )
    return {
      Kind: 'class',
      Name: x.name.text,
      Members: x.members.map(transformClassElement).filter(x => x != null),
      Extends: ext && ext.length && ext,
      Implements: impl && impl.length && impl
    }
  }
  if (ts.isTypeAliasDeclaration(x))
    return {
      Kind: 'typealias',
      Name: x.name.text,
      Type: transformType(x.type)
    }
  if (ts.isModuleDeclaration(x) && ts.isModuleBlock(x.body))
    return {
      Kind: 'module',
      Name: x.name.text,
      Members: x.body.statements.map(transformStatement).filter(x => x != null)
    }
  if (ts.isExportAssignment(x)) {
    return {
      Kind: 'exportassignment',
      Expression: x.expression.getText()
    }
  }
  if (ts.isExportDeclaration(x)) {
    return {
      Kind: 'exportdeclaration',
      Expression: x.getText()
    }
  }
  if (ts.isImportEqualsDeclaration(x)) {


  }
  unhandled(x, "Statement");
}

function transformFile(x: ts.SourceFile): TSFile {
  console.log("Found file: ", x.fileName)
  return {
    Name: x.fileName,
    Statements: x.statements.map(transformStatement).filter(x => x != null)
  }
}
