"""
IFC Helper Script using IfcOpenShell
Provides IFC model navigation and data extraction functionality for Unity C# integration.
"""

import sys
import json
import os
import traceback
import ifcopenshell
import ifcopenshell.util.element as element_util
import ifcopenshell.util.placement as placement_util
import ifcopenshell.util.shape as shape_util


class IFCModelHelper:
    """Helper class for loading and querying IFC models using IfcOpenShell."""
    
    def __init__(self):
        self.model = None
        self.file_path = None
        
        # Element caches
        self._elements_by_global_id = {}
        self._elements_by_tag = {}
        self._elements_by_name = {}
        
        # Spatial caches
        self._spatial_elements = []
        self._spatial_containment = {}  # Element GlobalId -> Spatial Container GlobalId
        
        # Relationship caches
        self._aggregates_children = {}  # Parent GlobalId -> List of Child GlobalIds
        self._aggregates_parent = {}    # Child GlobalId -> Parent GlobalId
        self._opening_to_host = {}      # Opening GlobalId -> Host Element GlobalId
        self._filling_to_opening = {}   # Door/Window GlobalId -> Opening GlobalId
        
    def load_model(self, ifc_path):
        """Load an IFC model from file path."""
        try:
            if not os.path.exists(ifc_path):
                return {"success": False, "error": f"IFC file not found: {ifc_path}"}
            
            self.model = ifcopenshell.open(ifc_path)
            self.file_path = ifc_path
            
            # Build caches
            self._build_element_caches()
            self._build_spatial_caches()
            self._build_relationship_caches()
            
            return {
                "success": True,
                "schema": self.model.schema,
                "elementCount": len(self._elements_by_global_id),
                "spatialElementCount": len(self._spatial_elements)
            }
        except Exception as e:
            return {"success": False, "error": str(e), "traceback": traceback.format_exc()}
    
    def _build_element_caches(self):
        """Build element lookup caches."""
        self._elements_by_global_id = {}
        self._elements_by_tag = {}
        self._elements_by_name = {}
        
        # Get all IFC products
        for product in self.model.by_type("IfcProduct"):
            global_id = product.GlobalId
            self._elements_by_global_id[global_id] = product
            
            # Cache by Tag
            if hasattr(product, 'Tag') and product.Tag:
                tag = product.Tag
                if tag not in self._elements_by_tag:
                    self._elements_by_tag[tag] = []
                self._elements_by_tag[tag].append(product)
            
            # Cache by Name
            if hasattr(product, 'Name') and product.Name:
                name = product.Name
                if name not in self._elements_by_name:
                    self._elements_by_name[name] = []
                self._elements_by_name[name].append(product)
    
    def _build_spatial_caches(self):
        """Build spatial structure caches."""
        self._spatial_elements = []
        self._spatial_containment = {}
        
        # Get all spatial structure elements
        spatial_types = [
            "IfcProject", "IfcSite", "IfcBuilding", "IfcBuildingStorey", 
            "IfcSpace", "IfcFacility", "IfcFacilityPart"
        ]
        
        for spatial_type in spatial_types:
            try:
                elements = self.model.by_type(spatial_type)
                self._spatial_elements.extend(elements)
            except:
                pass  # Type might not exist in this schema
        
        # Build containment relationships
        for rel in self.model.by_type("IfcRelContainedInSpatialStructure"):
            spatial_element = rel.RelatingStructure
            if spatial_element:
                spatial_global_id = spatial_element.GlobalId
                for element in rel.RelatedElements:
                    if element:
                        self._spatial_containment[element.GlobalId] = spatial_global_id
    
    def _build_relationship_caches(self):
        """Build relationship caches."""
        # Aggregation relationships
        for rel in self.model.by_type("IfcRelAggregates"):
            parent = rel.RelatingObject
            if parent:
                parent_id = parent.GlobalId
                self._aggregates_children[parent_id] = []
                for child in rel.RelatedObjects:
                    if child:
                        child_id = child.GlobalId
                        self._aggregates_children[parent_id].append(child_id)
                        self._aggregates_parent[child_id] = parent_id
        
        # Opening relationships
        for rel in self.model.by_type("IfcRelVoidsElement"):
            if rel.RelatingBuildingElement and rel.RelatedOpeningElement:
                host_id = rel.RelatingBuildingElement.GlobalId
                opening_id = rel.RelatedOpeningElement.GlobalId
                self._opening_to_host[opening_id] = host_id
        
        # Filling relationships (doors/windows in openings)
        for rel in self.model.by_type("IfcRelFillsElement"):
            if rel.RelatingOpeningElement and rel.RelatedBuildingElement:
                opening_id = rel.RelatingOpeningElement.GlobalId
                filling_id = rel.RelatedBuildingElement.GlobalId
                self._filling_to_opening[filling_id] = opening_id
    
    def get_element_by_global_id(self, global_id):
        """Get element by GlobalId."""
        try:
            element = self._elements_by_global_id.get(global_id)
            if element:
                return {"success": True, "element": self._serialize_element(element)}
            return {"success": False, "error": "Element not found"}
        except Exception as e:
            return {"success": False, "error": str(e)}
    
    def get_element_by_tag(self, tag):
        """Get element(s) by Tag."""
        try:
            elements = self._elements_by_tag.get(tag, [])
            if elements:
                return {
                    "success": True, 
                    "elements": [self._serialize_element(e) for e in elements]
                }
            return {"success": False, "error": "Element not found"}
        except Exception as e:
            return {"success": False, "error": str(e)}
    
    def get_element_by_name(self, name):
        """Get element(s) by Name."""
        try:
            elements = self._elements_by_name.get(name, [])
            if elements:
                return {
                    "success": True,
                    "elements": [self._serialize_element(e) for e in elements]
                }
            return {"success": False, "error": "Element not found"}
        except Exception as e:
            return {"success": False, "error": str(e)}
    
    def get_all_elements(self):
        """Get all IFC product elements."""
        try:
            elements = [self._serialize_element(e) for e in self._elements_by_global_id.values()]
            return {"success": True, "elements": elements, "count": len(elements)}
        except Exception as e:
            return {"success": False, "error": str(e)}
    
    def get_spatial_structure(self):
        """Get the complete spatial structure as a tree."""
        try:
            project = self.model.by_type("IfcProject")
            if not project:
                return {"success": False, "error": "No project found"}
            
            root = self._build_spatial_tree(project[0])
            return {"success": True, "structure": root}
        except Exception as e:
            return {"success": False, "error": str(e), "traceback": traceback.format_exc()}
    
    def _build_spatial_tree(self, element):
        """Recursively build spatial structure tree."""
        node = {
            "globalId": element.GlobalId,
            "name": element.Name if hasattr(element, 'Name') and element.Name else "",
            "longName": element.LongName if hasattr(element, 'LongName') and element.LongName else "",
            "ifcClass": element.is_a(),
            "children": []
        }
        
        # Get aggregated children
        if element.GlobalId in self._aggregates_children:
            for child_id in self._aggregates_children[element.GlobalId]:
                child = self._elements_by_global_id.get(child_id)
                if child:
                    node["children"].append(self._build_spatial_tree(child))
        
        # Get contained elements (for spatial structures)
        if element.is_a("IfcSpatialStructureElement") or element.is_a("IfcSpatialElement"):
            contained_elements = []
            for elem_id, spatial_id in self._spatial_containment.items():
                if spatial_id == element.GlobalId:
                    contained_elements.append(elem_id)
            node["containedElements"] = contained_elements
        
        return node
    
    def get_element_properties(self, global_id):
        """Get all properties for an element."""
        try:
            element = self._elements_by_global_id.get(global_id)
            if not element:
                return {"success": False, "error": "Element not found"}
            
            properties = self._extract_properties(element)
            return {"success": True, "properties": properties}
        except Exception as e:
            return {"success": False, "error": str(e), "traceback": traceback.format_exc()}
    
    def _extract_properties(self, element):
        """Extract all properties from an element."""
        properties = {}
        
        # Basic identity properties
        properties["Identity.GlobalId"] = element.GlobalId
        properties["Identity.Name"] = element.Name if hasattr(element, 'Name') else ""
        properties["Identity.Description"] = element.Description if hasattr(element, 'Description') else ""
        properties["Identity.IfcClass"] = element.is_a()
        properties["Identity.Tag"] = element.Tag if hasattr(element, 'Tag') else ""
        properties["Identity.ObjectType"] = element.ObjectType if hasattr(element, 'ObjectType') else ""
        
        # Get property sets
        psets = element_util.get_psets(element)
        for pset_name, pset_data in psets.items():
            if isinstance(pset_data, dict):
                for prop_name, prop_value in pset_data.items():
                    if prop_name not in ['id', 'type']:  # Skip metadata
                        key = f"{pset_name}.{prop_name}"
                        properties[key] = self._serialize_value(prop_value)
        
        # Get quantities
        quantities = element_util.get_quantity(element)
        if quantities:
            for qto_name, qto_data in quantities.items():
                if isinstance(qto_data, dict):
                    for qty_name, qty_value in qto_data.items():
                        if qty_name not in ['id', 'type']:
                            key = f"{qto_name}.{qty_name}"
                            properties[key] = self._serialize_value(qty_value)
        
        # Get material
        material = element_util.get_material(element)
        if material:
            if hasattr(material, 'Name'):
                properties["Material.Name"] = material.Name if material.Name else ""
        
        # Get type
        element_type = element_util.get_type(element)
        if element_type:
            properties["Type.Name"] = element_type.Name if hasattr(element_type, 'Name') else ""
            properties["Type.Description"] = element_type.Description if hasattr(element_type, 'Description') else ""
        
        # Spatial containment
        if element.GlobalId in self._spatial_containment:
            spatial_id = self._spatial_containment[element.GlobalId]
            spatial_elem = self._elements_by_global_id.get(spatial_id)
            if spatial_elem:
                properties["Location.SpatialContainer"] = spatial_elem.Name if hasattr(spatial_elem, 'Name') else spatial_id
                properties["Location.SpatialContainerGlobalId"] = spatial_id
        
        return properties
    
    def _serialize_element(self, element):
        """Serialize an IFC element to a dictionary."""
        return {
            "globalId": element.GlobalId,
            "ifcClass": element.is_a(),
            "name": element.Name if hasattr(element, 'Name') and element.Name else "",
            "description": element.Description if hasattr(element, 'Description') and element.Description else "",
            "tag": element.Tag if hasattr(element, 'Tag') and element.Tag else "",
            "objectType": element.ObjectType if hasattr(element, 'ObjectType') and element.ObjectType else ""
        }
    
    def _serialize_value(self, value):
        """Serialize a property value to string."""
        if value is None:
            return ""
        if isinstance(value, (list, tuple)):
            return ", ".join(str(v) for v in value)
        return str(value)
    
    def get_project_info(self):
        """Get project information."""
        try:
            project = self.model.by_type("IfcProject")
            if not project:
                return {"success": False, "error": "No project found"}
            
            project = project[0]
            return {
                "success": True,
                "globalId": project.GlobalId,
                "name": project.Name if project.Name else "",
                "longName": project.LongName if hasattr(project, 'LongName') and project.LongName else "",
                "description": project.Description if project.Description else ""
            }
        except Exception as e:
            return {"success": False, "error": str(e)}
    
    def close_model(self):
        """Close the current model and clear caches."""
        self.model = None
        self.file_path = None
        self._elements_by_global_id.clear()
        self._elements_by_tag.clear()
        self._elements_by_name.clear()
        self._spatial_elements.clear()
        self._spatial_containment.clear()
        self._aggregates_children.clear()
        self._aggregates_parent.clear()
        return {"success": True}


def main():
    """Main entry point for command-line usage."""
    if len(sys.argv) < 3:
        print(json.dumps({"success": False, "error": "Usage: python ifc_helper.py <command> <args...>"}))
        return
    
    command = sys.argv[1]
    helper = IFCModelHelper()
    
    try:
        if command == "load_model":
            ifc_path = sys.argv[2]
            result = helper.load_model(ifc_path)
            print(json.dumps(result))
        
        elif command == "get_element":
            ifc_path = sys.argv[2]
            global_id = sys.argv[3]
            helper.load_model(ifc_path)
            result = helper.get_element_by_global_id(global_id)
            print(json.dumps(result))
        
        elif command == "get_element_by_tag":
            ifc_path = sys.argv[2]
            tag = sys.argv[3]
            helper.load_model(ifc_path)
            result = helper.get_element_by_tag(tag)
            print(json.dumps(result))
        
        elif command == "get_properties":
            ifc_path = sys.argv[2]
            global_id = sys.argv[3]
            helper.load_model(ifc_path)
            result = helper.get_element_properties(global_id)
            print(json.dumps(result))
        
        elif command == "get_spatial_structure":
            ifc_path = sys.argv[2]
            helper.load_model(ifc_path)
            result = helper.get_spatial_structure()
            print(json.dumps(result))
        
        elif command == "get_all_elements":
            ifc_path = sys.argv[2]
            helper.load_model(ifc_path)
            result = helper.get_all_elements()
            print(json.dumps(result))
        
        else:
            print(json.dumps({"success": False, "error": f"Unknown command: {command}"}))
    
    except Exception as e:
        print(json.dumps({"success": False, "error": str(e), "traceback": traceback.format_exc()}))


if __name__ == "__main__":
    main()
