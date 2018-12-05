namespace Types

type Id = int
type Error = int*string

type UnitWithChildren = {
    Name: string
    CmsId: string
    Children: UnitWithChildren[]
}

