namespace ServicioCatalogo.Soap;

/// <summary>Contrato WSDL 1.1 explícito del servicio de catálogo.</summary>
public static class Wsdl
{
    public static string Generar(string ubicacion) => Plantilla.Replace("{UBICACION}", System.Security.SecurityElement.Escape(ubicacion));

    private const string Plantilla = """
<?xml version="1.0" encoding="utf-8"?>
<wsdl:definitions name="CatalogoService"
    targetNamespace="http://ucu.edu.uy/tfu5/catalogo"
    xmlns:wsdl="http://schemas.xmlsoap.org/wsdl/"
    xmlns:soap="http://schemas.xmlsoap.org/wsdl/soap/"
    xmlns:xs="http://www.w3.org/2001/XMLSchema"
    xmlns:tns="http://ucu.edu.uy/tfu5/catalogo">

  <wsdl:types>
    <xs:schema targetNamespace="http://ucu.edu.uy/tfu5/catalogo" elementFormDefault="qualified">
      <xs:complexType name="Producto">
        <xs:sequence>
          <xs:element name="id" type="xs:string"/>
          <xs:element name="nombre" type="xs:string"/>
          <xs:element name="precio" type="xs:decimal"/>
        </xs:sequence>
      </xs:complexType>

      <xs:complexType name="ItemReserva">
        <xs:sequence>
          <xs:element name="productoId" type="xs:string"/>
          <xs:element name="cantidad" type="xs:int"/>
        </xs:sequence>
      </xs:complexType>

      <xs:element name="GetProductoRequest">
        <xs:complexType><xs:sequence>
          <xs:element name="id" type="xs:string"/>
        </xs:sequence></xs:complexType>
      </xs:element>
      <xs:element name="GetProductoResponse">
        <xs:complexType><xs:sequence>
          <xs:element name="producto" type="tns:Producto"/>
        </xs:sequence></xs:complexType>
      </xs:element>

      <xs:element name="ReservarStockRequest">
        <xs:complexType><xs:sequence>
          <xs:element name="pedidoId" type="xs:string"/>
          <xs:element name="items">
            <xs:complexType><xs:sequence>
              <xs:element name="item" type="tns:ItemReserva" maxOccurs="unbounded"/>
            </xs:sequence></xs:complexType>
          </xs:element>
        </xs:sequence></xs:complexType>
      </xs:element>
      <xs:element name="ReservarStockResponse">
        <xs:complexType><xs:sequence>
          <xs:element name="pedidoId" type="xs:string"/>
          <xs:element name="estado" type="xs:string"/>
          <xs:element name="yaProcesada" type="xs:boolean"/>
        </xs:sequence></xs:complexType>
      </xs:element>

      <xs:element name="LiberarStockRequest">
        <xs:complexType><xs:sequence>
          <xs:element name="pedidoId" type="xs:string"/>
        </xs:sequence></xs:complexType>
      </xs:element>
      <xs:element name="LiberarStockResponse">
        <xs:complexType><xs:sequence>
          <xs:element name="pedidoId" type="xs:string"/>
          <xs:element name="estado" type="xs:string"/>
        </xs:sequence></xs:complexType>
      </xs:element>

      <xs:element name="CatalogoFault">
        <xs:complexType><xs:sequence>
          <xs:element name="codigo" type="xs:string"/>
        </xs:sequence></xs:complexType>
      </xs:element>
    </xs:schema>
  </wsdl:types>

  <wsdl:message name="GetProductoInput"><wsdl:part name="parameters" element="tns:GetProductoRequest"/></wsdl:message>
  <wsdl:message name="GetProductoOutput"><wsdl:part name="parameters" element="tns:GetProductoResponse"/></wsdl:message>
  <wsdl:message name="ReservarStockInput"><wsdl:part name="parameters" element="tns:ReservarStockRequest"/></wsdl:message>
  <wsdl:message name="ReservarStockOutput"><wsdl:part name="parameters" element="tns:ReservarStockResponse"/></wsdl:message>
  <wsdl:message name="LiberarStockInput"><wsdl:part name="parameters" element="tns:LiberarStockRequest"/></wsdl:message>
  <wsdl:message name="LiberarStockOutput"><wsdl:part name="parameters" element="tns:LiberarStockResponse"/></wsdl:message>
  <wsdl:message name="CatalogoFaultMessage"><wsdl:part name="detail" element="tns:CatalogoFault"/></wsdl:message>

  <wsdl:portType name="CatalogoPortType">
    <wsdl:operation name="GetProducto">
      <wsdl:input message="tns:GetProductoInput"/>
      <wsdl:output message="tns:GetProductoOutput"/>
      <wsdl:fault name="CatalogoFault" message="tns:CatalogoFaultMessage"/>
    </wsdl:operation>
    <wsdl:operation name="ReservarStock">
      <wsdl:input message="tns:ReservarStockInput"/>
      <wsdl:output message="tns:ReservarStockOutput"/>
      <wsdl:fault name="CatalogoFault" message="tns:CatalogoFaultMessage"/>
    </wsdl:operation>
    <wsdl:operation name="LiberarStock">
      <wsdl:input message="tns:LiberarStockInput"/>
      <wsdl:output message="tns:LiberarStockOutput"/>
      <wsdl:fault name="CatalogoFault" message="tns:CatalogoFaultMessage"/>
    </wsdl:operation>
  </wsdl:portType>

  <wsdl:binding name="CatalogoSoapBinding" type="tns:CatalogoPortType">
    <soap:binding style="document" transport="http://schemas.xmlsoap.org/soap/http"/>
    <wsdl:operation name="GetProducto">
      <soap:operation soapAction="http://ucu.edu.uy/tfu5/catalogo/GetProducto"/>
      <wsdl:input><soap:body use="literal"/></wsdl:input>
      <wsdl:output><soap:body use="literal"/></wsdl:output>
      <wsdl:fault name="CatalogoFault"><soap:fault name="CatalogoFault" use="literal"/></wsdl:fault>
    </wsdl:operation>
    <wsdl:operation name="ReservarStock">
      <soap:operation soapAction="http://ucu.edu.uy/tfu5/catalogo/ReservarStock"/>
      <wsdl:input><soap:body use="literal"/></wsdl:input>
      <wsdl:output><soap:body use="literal"/></wsdl:output>
      <wsdl:fault name="CatalogoFault"><soap:fault name="CatalogoFault" use="literal"/></wsdl:fault>
    </wsdl:operation>
    <wsdl:operation name="LiberarStock">
      <soap:operation soapAction="http://ucu.edu.uy/tfu5/catalogo/LiberarStock"/>
      <wsdl:input><soap:body use="literal"/></wsdl:input>
      <wsdl:output><soap:body use="literal"/></wsdl:output>
      <wsdl:fault name="CatalogoFault"><soap:fault name="CatalogoFault" use="literal"/></wsdl:fault>
    </wsdl:operation>
  </wsdl:binding>

  <wsdl:service name="CatalogoService">
    <wsdl:port name="CatalogoPort" binding="tns:CatalogoSoapBinding">
      <soap:address location="{UBICACION}"/>
    </wsdl:port>
  </wsdl:service>
</wsdl:definitions>
""";
}
